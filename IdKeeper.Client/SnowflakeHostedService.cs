using IdGen;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static IdKeeper.Client.IdKeeperApiClient;

namespace IdKeeper.Client;

internal class SnowflakeHostedService : BackgroundService, ISnowflakeIdGenerator
{
	private sealed class GeneratorSlot(IdGenerator generator)
	{
		public IdGenerator Generator { get; } = generator;
		public SemaphoreSlim Lock { get; } = new(1, 1);
	}

	// 한 번에 발급 가능한 최대 ID 수. 호출부의 입력 검증과 공유하기 위해 공개 상수로 노출한다.
	public const Int32 MaxAllocateCount = SnowflakeIdLimits.MaxAllocateCount;

	// 갱신 재시도 간격이 아무리 좁혀져도 이보다 짧아지지 않는다 (busy loop 방지).
	private static readonly TimeSpan s_minLoopDelay = TimeSpan.FromSeconds(5);

	// 초기 할당(Alloc) 재시도 백오프. NextInitRetryDelay 참고.
	private static readonly TimeSpan s_initialRetryDelay = TimeSpan.FromSeconds(3);
	private static readonly TimeSpan s_maxInitRetryDelay = TimeSpan.FromSeconds(60);
	private const double InitRetryJitterRatio = 0.2;

	// 갱신이 밀린 상태에서 만료까지 남은 시간을 이 횟수로 나눠 재시도 간격을 정한다.
	private const Int32 MinRenewAttempts = 4;

	// 한 프로세스에 발급기가 둘 이상 기동하는 것을 막는 전역 가드. DI 등록은
	// AddIdKeeperSnowflake가 멱등하게 막지만, 한 프로세스에 호스트를 둘 띄우면 컨테이너가
	// 달라 DI로는 막을 수 없다. 그 경우 같은 requester로 Alloc이 두 번 일어나고 멱등 동작 탓에
	// 동일한 노드 Id를 받아 ID가 중복되므로, 기동 시점에 실패시킨다.
	private static Int32 s_runningInstanceCount;

	private readonly ILogger _logger;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly SnowflakeClientOptions _options;
	private readonly IHostApplicationLifetime _hostLifetime;

	// _initLock: InitializeAsync / RenewAsync / RemoveAsync 만 취득.
	// AllocateIdAsync는 취득하지 않음 — Volatile.Read + 슬롯별 Lock으로 동작.
	private readonly SemaphoreSlim _initLock = new(1, 1);
	private GeneratorSlot[]? _generatorSlots;
	private Int32 _nextSlot;
	private Int32 _allocatingCount;
	private TaskCompletionSource? _drainTcs;
	// 두 시각 모두 RenewLoop 외의 스레드에서도 접근한다 (_expiredAtUtcTicks는
	// AllocateIdCoreAsync의 임의 스레드, _renewAtUtcTicks는 셧다운 스레드의 RemoveAsync).
	// torn read 방지와 가시성 보장을 위해 ticks(Int64) + Volatile로 통일해 다룬다.
	private Int64 _renewAtUtcTicks = DateTime.MaxValue.Ticks;
	private Int64 _expiredAtUtcTicks = DateTime.MaxValue.Ticks;

	public SnowflakeHostedService(
		ILogger<SnowflakeHostedService> logger,
		IServiceScopeFactory scopeFactory,
		SnowflakeClientOptions options,
		IHostApplicationLifetime hostLifetime)
	{
		_logger = logger;
		_scopeFactory = scopeFactory;
		_options = options;
		_hostLifetime = hostLifetime;

		// requester는 프로세스 인스턴스마다 유일해야 한다 — SnowflakeClientOptions.Requester 주석 참고.
		_requester = string.IsNullOrWhiteSpace(options.Requester)
			? SnowflakeClientIdentity.Current
			: options.Requester;
	}

	private readonly string _requester;

	/// <summary>이 프로세스가 서버에 자신을 식별시키는 값. 진단 로깅용으로 노출한다.</summary>
	public string Requester => _requester;

	private bool _started;

	public override Task StartAsync(CancellationToken cancellationToken)
	{
		if (Interlocked.Increment(ref s_runningInstanceCount) > 1)
		{
			Interlocked.Decrement(ref s_runningInstanceCount);
			throw new InvalidOperationException(
				"IdKeeper Snowflake client is already running in this process." +
				" Only one instance is allowed — two generator sets would receive the same node ids" +
				" (Alloc is idempotent per requester) and emit duplicate Snowflake ids." +
				" Call AddIdKeeperSnowflake() once, and do not build a second host in the same process.");
		}

		_started = true;
		return base.StartAsync(cancellationToken);
	}

	// 슬롯 존재 여부와 리스 유효성을 함께 본다 — AllocateIdCoreAsync가 발급을 허용하는 조건과
	// 동일해야 한다. 만료된 슬롯을 실제로 내리는 건 RenewLoop의 다음 주기이므로, 슬롯만 보면
	// 최대 RenewLoopDuration 동안 헬스체크는 Healthy인데 모든 발급이 503이 되는 구간이 생긴다.
	public Task<bool> IsReadyAsync(CancellationToken _)
		=> Task.FromResult(
			Volatile.Read(ref _generatorSlots) is not null
			&& DateTime.UtcNow.Ticks < Volatile.Read(ref _expiredAtUtcTicks));

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		await InitializeAsync(stoppingToken);
		if (stoppingToken.IsCancellationRequested)
		{
			return;
		}

		try
		{
			await RenewLoopAsync(stoppingToken);
		}
		catch (SnowflakeRuntimeException ex)
		{
			_logger.LogCritical(ex, "Unhandled exception in Snowflake renew loop; stopping application.");
			_hostLifetime.StopApplication();
		}
	}

	/// <summary>
	/// 초기 할당 재시도 간격을 계산한다 (3s, 6s, 12s, 24s, 48s, 이후 60s 상한).
	///
	/// 고정 간격이면 지속 실패 시 로그가 과도하게 쌓인다 — 실패 1회당 두 줄(HTTP 오류 + 이 루프의
	/// 경고)이 남으므로 3초 고정은 하루 약 5.7만 줄이다. 반대로 상한을 크게 잡으면 원인이 해소된
	/// 뒤 복구가 늦어지므로, 초기에는 촘촘히 시도해 문제를 즉시 드러내고 상한은 60초로 묶는다.
	///
	/// 지터를 섞는 이유: 롤링 배포로 여러 인스턴스가 동시에 기동해 같은 실패(서버 장애, 시계 오차
	/// 등)를 겪으면 재시도가 같은 시점에 겹쳐 서버로 몰린다.
	/// </summary>
	private static TimeSpan NextInitRetryDelay(Int32 attempt)
	{
		// 지수가 커져 double이 무한대가 되지 않도록 지수를 먼저 제한한다.
		Int32 exponent = Math.Min(Math.Max(attempt - 1, 0), 16);
		double seconds = s_initialRetryDelay.TotalSeconds * Math.Pow(2, exponent);

		TimeSpan delay = seconds >= s_maxInitRetryDelay.TotalSeconds
			? s_maxInitRetryDelay
			: TimeSpan.FromSeconds(seconds);

		double jitter = 1.0 + ((Random.Shared.NextDouble() * 2.0 - 1.0) * InitRetryJitterRatio);
		return delay * jitter;
	}

	private async Task InitializeAsync(CancellationToken cancellationToken)
	{
		Int32 attempt = 0;

		while (!cancellationToken.IsCancellationRequested)
		{
			attempt++;
			TimeSpan retryDelay = NextInitRetryDelay(attempt);
			bool acquired = false;
			try
			{
				await _initLock.WaitAsync(cancellationToken);
				acquired = true;

				if (_generatorSlots is not null)
				{
					return;
				}

				await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
				IdKeeperApiClient idKeeperApiClient =
					scope.ServiceProvider.GetRequiredService<IdKeeperApiClient>();

				Int32 requestCount = _options.GeneratorCount;
				RequestV1Alloc requestAlloc = new(
					Count: requestCount, _requester, DateTimeOffset.UtcNow);
				ResponseV1Alloc? responseAlloc =
					await idKeeperApiClient.PostIdKeeperAlloc(requestAlloc, cancellationToken);

				if (responseAlloc is null || responseAlloc.Ids.Count == 0)
				{
					_logger.LogWarning(
						"Failed to alloc node id (attempt {Attempt}), retrying in {DelayMs:F0}ms",
						attempt,
						retryDelay.TotalMilliseconds);
				}
				else
				{
					const Int32 MaxBitCount = 63;
					ResponseV1Alloc.BitCountRecord bitCount = responseAlloc.BitCount;
					Int32 sum = bitCount.Timestamp + bitCount.NodeId + bitCount.SequenceId;
					// 합계만 검증하면 음수가 섞여도 통과해 아래 byte 캐스트에서 잘린 값이
					// 들어갈 수 있다. 각 비트 수가 양수임을 함께 검증해야 byte 캐스트가
					// 안전하다 (각각 ≤ 61 < 255).
					if (bitCount.Timestamp <= 0 || bitCount.NodeId <= 0
						|| bitCount.SequenceId <= 0 || sum != MaxBitCount)
					{
						_logger.LogCritical(
							"Invalid BitCount {{{BitCount}}}: each must be positive" +
							" and sum must be {Max}. Stopping application.",
							bitCount,
							MaxBitCount);
						_hostLifetime.StopApplication();
						return;
					}

					// 노드 ID가 NodeId 비트 수 범위를 벗어나면 IdGenerator가 다른 노드와
					// 겹치는 ID를 생성할 수 있으므로 fail-fast.
					Int64 maxNodeId = (1L << bitCount.NodeId) - 1;
					if (responseAlloc.Ids.Any(r => r.Id < 0 || r.Id > maxNodeId))
					{
						_logger.LogCritical(
							"Allocated node id out of range [0, {MaxNodeId}]: [{Ids}]." +
							" Stopping application.",
							maxNodeId,
							string.Join(", ", responseAlloc.Ids.Select(r => r.Id)));
						_hostLifetime.StopApplication();
						return;
					}

					if (responseAlloc.Ids.Count < requestCount)
					{
						_logger.LogWarning(
							"Requested {Requested} node ids but only {Actual} were allocated.",
							requestCount,
							responseAlloc.Ids.Count);
					}

					_logger.LogInformation("ResponseAlloc: {ResponseAlloc}", responseAlloc);

					IdGeneratorOptions options = new(
						new IdStructure(
							(byte)bitCount.Timestamp,
							(byte)bitCount.NodeId,
							(byte)bitCount.SequenceId),
						new DefaultTimeSource(responseAlloc.BaseDateTime),
						SequenceOverflowStrategy.SpinWait);

					GeneratorSlot[] slots = responseAlloc.Ids
						.OrderBy(r => r.Id)
						.Select(r => new GeneratorSlot(new IdGenerator(r.Id, options)))
						.ToArray();

					DateTime utcNow = DateTime.UtcNow;
					DateTime expiredAtUtc = responseAlloc.Ids.Min(r => r.ExpiredAtUtc).UtcDateTime;

					// 방금 받은 리스가 로컬 시계 기준으로 이미 만료라면 두 시계의 괴리가 리스
					// 길이를 넘어선 것이므로, 발급이 즉시 전량 차단되는 상태로 기동하지 않도록
					// fail-fast 한다.
					// 주의: 이 검사가 잡는 건 로컬 시계가 서버보다 '앞선' 방향뿐이다. 로컬이
					// '뒤처진' 경우 (만료 - 로컬now)가 오히려 리스보다 커져 항상 통과한다 —
					// 클라이언트가 로컬 정보만으로 그 방향을 검출할 수 없다는 사실은 그대로다.
					// 대신 요청에 ClientUtcNow를 실어 보내 서버가 판정한다: 뒤처짐이
					// CleanupGracePeriod를 넘으면 서버가 Alloc을 409로 거부하므로 여기까지
					// 오지 않는다(Renew는 거부하지 않고 경고만 한다).
					// 따라서 이 로컬 검사는 '앞선' 방향 전용 가드로 남는다.
					if (expiredAtUtc <= utcNow)
					{
						_logger.LogCritical(
							"Allocated lease is already expired by the local clock" +
							" (expiredAtUtc={ExpiredAtUtc:O}, localUtcNow={UtcNow:O})." +
							" Check clock synchronization. Stopping application.",
							expiredAtUtc,
							utcNow);
						_hostLifetime.StopApplication();
						return;
					}

					Volatile.Write(ref _expiredAtUtcTicks, expiredAtUtc.Ticks);
					// 의도된 동작: 갱신 시점을 현재 시각으로 설정해 첫 RenewLoop 진입 시
					// 즉시 Renew를 1회 수행한다. 시작 직후 갱신 경로가 정상인지 조기에
					// 검증하고, 이후 갱신 시점은 RenewAsync가 만료 시각의 절반 지점으로
					// 재계산한다.
					Volatile.Write(ref _renewAtUtcTicks, utcNow.Ticks);

					Volatile.Write(ref _generatorSlots, slots);

					_logger.LogInformation(
						"SnowflakeHostedService initialized successfully" +
						" (attempt {Attempt}, generators {Count}).",
						attempt,
						slots.Length);
					return;
				}
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Exception in InitializeAsync (attempt {Attempt}).", attempt);
			}
			finally
			{
				if (acquired)
				{
					_initLock.Release();
				}
			}

			try
			{
				await Task.Delay(retryDelay, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}
		}
	}

	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		_logger.LogInformation("Stop SnowflakeHostedService.");
		await base.StopAsync(cancellationToken);
		await RemoveAsync(CancellationToken.None);
	}

	// ── ISnowflakeIdGenerator (소비자 표면) ───────────────────────────────────────

	/// <inheritdoc />
	public bool IsReady
		=> Volatile.Read(ref _generatorSlots) is not null
			&& DateTime.UtcNow.Ticks < Volatile.Read(ref _expiredAtUtcTicks);

	/// <inheritdoc />
	public Int64 NextId()
	{
		GeneratorSlot[] slots = RequireReadySlots();

		// count=1은 슬롯 락을 잡지 않는다. IdGen.IdGenerator는 자체적으로 스레드 안전이고
		// (내부 lock), 이 경로는 다음 밀리초를 기다릴 일이 사실상 없어 비동기 대기로 바꿔
		// 얻을 이득이 없다. 세마포어는 대량 배치가 락을 오래 쥘 때 대기자가 스레드풀 스레드를
		// 반납하도록 하는 용도라 여기서는 불필요하다.
		Int32 n = slots.Length;
		Int32 index = (Int32)((UInt32)Interlocked.Increment(ref _nextSlot) % (UInt32)n);
		Int64 id = slots[index].Generator.CreateId();

		// Take가 다음 밀리초를 기다리는 사이 리스가 만료됐을 수 있다 — 이미 만료된 노드 Id로
		// 만든 값은 다른 프로세스와 겹칠 수 있으므로 버린다.
		EnsureStillValid();
		return id;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<Int64>> NextIdsAsync(
		Int32 count, CancellationToken cancellationToken = default)
	{
		IReadOnlyList<Int64> ids = await AllocateIdAsync(count, cancellationToken);
		if (ids.Count == 0)
		{
			throw NotReady();
		}
		return ids;
	}

	private GeneratorSlot[] RequireReadySlots()
	{
		GeneratorSlot[]? slots = Volatile.Read(ref _generatorSlots);
		if (slots is null || slots.Length == 0)
		{
			throw NotReady();
		}

		EnsureStillValid();
		return slots;
	}

	private void EnsureStillValid()
	{
		if (Volatile.Read(ref _expiredAtUtcTicks) <= DateTime.UtcNow.Ticks)
		{
			throw NotReady();
		}
	}

	private static SnowflakeNotReadyException NotReady()
		=> new("IdKeeper Snowflake client is not ready: the node id lease has not been acquired yet," +
			" or it has expired and id issuance is blocked.");

	// ── 내부 구현 ────────────────────────────────────────────────────────────────

	public async Task<IReadOnlyList<Int64>> AllocateIdAsync(Int32 count, CancellationToken cancellationToken)
	{
		// 컨트롤러 DTO([Range(1, MaxAllocateCount)])가 1차 방어선이지만 public 메서드이므로
		// 내부 호출자의 잘못된 count(거대 배열 할당 + 장시간 락 점유)도 차단한다.
		// 이로써 빈 배열 반환은 "서비스 사용 불가(초기화 전/종료 중/리스 만료)"만을 의미한다.
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(count, MaxAllocateCount);

		Interlocked.Increment(ref _allocatingCount);
		try
		{
			return await AllocateIdCoreAsync(count, cancellationToken);
		}
		finally
		{
			if (Interlocked.Decrement(ref _allocatingCount) == 0)
				Volatile.Read(ref _drainTcs)?.TrySetResult();
		}
	}

	private async Task<IReadOnlyList<Int64>> AllocateIdCoreAsync(
		Int32 count, CancellationToken cancellationToken)
	{
		GeneratorSlot[]? slots = Volatile.Read(ref _generatorSlots);
		if (slots is null || slots.Length == 0) return [];

		// 리스가 만료되면 RenewLoop가 감지하기 전이라도 발급을 차단한다.
		// 만료된 노드 ID는 서버가 다른 프로세스에 재할당할 수 있어 ID 중복 위험이 있다.
		// 검사 통과 직후 만료가 지날 수 있는 best-effort 검사지만, 만료 절반 시점에
		// 갱신하는 리스 설계상 그 마진 안에서 허용된다.
		if (Volatile.Read(ref _expiredAtUtcTicks) <= DateTime.UtcNow.Ticks) return [];

		Int32 n = slots.Length;
		// 모든 슬롯은 InitializeAsync에서 단일 IdGeneratorOptions로 생성되므로
		// slots[0]의 값이 전체를 대표한다.
		Int32 maxSeqPerMs = slots[0].Generator.Options.IdStructure.MaxSequenceIds;
		// 올림 나눗셈은 Int64로 계산해 count + maxSeqPerMs - 1의 오버플로를 방지한다.
		Int32 genCount = (Int32)Math.Min(n, (count + (Int64)maxSeqPerMs - 1) / maxSeqPerMs);

		// 사용할 슬롯 수만큼 카운터를 전진시켜, 연속된 멀티청크 요청이 서로 다른
		// 슬롯 집합으로 분산되도록 한다 (슬롯 집합이 겹치며 생기는 락 경합 완화).
		Int32 reserved = Interlocked.Add(ref _nextSlot, genCount) - genCount;
		Int32 start = (Int32)((UInt32)reserved % (UInt32)n);

		Int64[] result;
		if (genCount == 1)
		{
			result = await TakeFromSlotAsync(slots[start], count, cancellationToken);
		}
		else
		{
			Int32 baseChunk = count / genCount;
			Int32 remainder = count % genCount;

			// SemaphoreSlim.WaitAsync()는 비경합 시 이미 완료된 Task를 반환하므로
			// await가 yield하지 않아 직접 호출하면 루프가 순차 실행된다.
			// 앞의 N-1개 청크만 Task.Run으로 분리하고 마지막 청크는 호출 스레드가
			// 직접 처리해 genCount=2인 일반적인 경우 Task.Run을 1개로 최소화한다.
			Task<Int64[]>[] tasks = new Task<Int64[]>[genCount];
			for (Int32 i = 0; i < genCount - 1; ++i)
			{
				Int32 chunk = baseChunk + (i < remainder ? 1 : 0);
				GeneratorSlot slot = slots[(start + i) % n];
				tasks[i] = Task.Run(
					() => TakeFromSlotAsync(slot, chunk, cancellationToken),
					cancellationToken);
			}

			Int32 lastChunk = baseChunk + (genCount - 1 < remainder ? 1 : 0);
			tasks[genCount - 1] =
				TakeFromSlotAsync(slots[(start + genCount - 1) % n], lastChunk, cancellationToken);

			// Task.WhenAll은 모든 task가 완료된 뒤에 예외를 던진다.
			// 일부 청크가 실패하면 전체를 실패 처리하고, 이미 소비된 ID는 gap으로 버린다.
			Int64[][] chunks = await Task.WhenAll(tasks);

			// 청크 합계는 정확히 count이므로 미리 크기를 잡아 재할당 없이 병합한다.
			result = new Int64[count];
			Int32 offset = 0;
			foreach (Int64[] chunk in chunks)
			{
				chunk.CopyTo(result, offset);
				offset += chunk.Length;
			}

			// 의도된 동작: 반환 ID는 오름차순 정렬을 보장한다. 멀티 제너레이터 병합 시
			// 서로 다른 nodeId가 섞여 순서가 보장되지 않으므로 정렬한다.
			// (단일 제너레이터 경로는 IdGen이 단조 증가를 보장하므로 정렬이 불필요하다.)
			Array.Sort(result);
		}

		// Take()가 SpinWait로 지연되는 동안 리스가 만료됐을 수 있다. RenewLoop의
		// 만료 경로(만료 즉시 슬롯을 내리는 쪽)는 drain을 기다리지 않으므로 여기서
		// 재검사해야 한다 — 이미 소비된 ID는 gap으로 버리는 편이 다른 프로세스와의
		// 중복 발급보다 안전하다.
		if (Volatile.Read(ref _expiredAtUtcTicks) <= DateTime.UtcNow.Ticks) return [];

		return result;
	}

	private static async Task<Int64[]> TakeFromSlotAsync(
		GeneratorSlot slot, Int32 count, CancellationToken cancellationToken)
	{
		// 취소 토큰은 락 대기에만 적용되고 락 획득 후 Take(count)는 취소되지 않는다.
		// 청크 크기가 MaxAllocateCount로 제한되어 ms 단위에 끝나므로 실질 영향은 없다.
		await slot.Lock.WaitAsync(cancellationToken);
		try
		{
			return slot.Generator.Take(count).ToArray();
		}
		finally
		{
			slot.Lock.Release();
		}
	}

	private async Task RemoveAsync(CancellationToken cancellationToken)
	{
		bool acquired = false;
		try
		{
			await _initLock.WaitAsync(cancellationToken);
			acquired = true;

			if (_generatorSlots is null)
			{
				return;
			}

			// 슬롯을 먼저 null로 교체 — 새 AllocateIdAsync 호출은 즉시 [] 반환.
			TaskCompletionSource drainTcs =
				new(TaskCreationOptions.RunContinuationsAsynchronously);
			Volatile.Write(ref _drainTcs, drainTcs);
			Volatile.Write(ref _generatorSlots, null);
			Volatile.Write(ref _expiredAtUtcTicks, DateTime.MaxValue.Ticks);
			Volatile.Write(ref _renewAtUtcTicks, DateTime.MaxValue.Ticks);

			// 이미 슬롯을 캡처한 진행 중 AllocateIdAsync가 끝날 때까지 대기.
			// 드레인 완료 후에야 서버로 노드 ID를 반납 — 유니크니스 보장.
			if (Volatile.Read(ref _allocatingCount) == 0)
				drainTcs.TrySetResult();
			await drainTcs.Task;
			Volatile.Write(ref _drainTcs, null);

			// 슬롯 락(SemaphoreSlim)은 dispose하지 않는다 — AvailableWaitHandle을 쓰지
			// 않는 한 dispose는 사실상 불필요하며, 이전에는 drain이 끝나지 않은 채 여기
			// 또는 Dispose()가 먼저 실행되면 아직 락 대기 중인 TakeFromSlotAsync의
			// Release()가 ObjectDisposedException을 던질 수 있는 경합이 있었다.
			await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
			IdKeeperApiClient idKeeperApiClient =
				scope.ServiceProvider.GetRequiredService<IdKeeperApiClient>();

			RequestV1Remove requestRemove = new(_requester);
			ResponseV1Remove? responseRemove =
				await idKeeperApiClient.PostIdKeeperRemove(requestRemove, cancellationToken);
			if (responseRemove is null)
			{
				// RemoveAsync는 종료 경로에서 호출되므로 fail-fast로 앱을 죽이지 않고
				// 로깅 후 정상 반환한다. (SnowflakeRuntimeException은 RenewLoop의
				// fail-fast 신호 전용 — 여기서 던지면 아래 catch에 즉시 삼켜진다.)
				_logger.LogError("Fail to remove node id. Check error logs.");
				return;
			}

			_logger.LogInformation("ResponseRemove {ResponseRemove}", responseRemove);
		}
		catch (OperationCanceledException)
		{
			// Ignore cancellation
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Exception in RemoveAsync.");
		}
		finally
		{
			if (acquired)
			{
				_initLock.Release();
			}
		}
	}

	private async Task RenewAsync(CancellationToken cancellationToken)
	{
		bool acquired = false;
		try
		{
			await _initLock.WaitAsync(cancellationToken);
			acquired = true;

			if (_generatorSlots is null)
			{
				throw new SnowflakeRuntimeException("Cannot renew: GeneratorSlots is null.");
			}

			await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
			IdKeeperApiClient idKeeperApiClient =
				scope.ServiceProvider.GetRequiredService<IdKeeperApiClient>();

			RequestV1Renew requestRenew = new(_requester, DateTimeOffset.UtcNow);
			ResponseV1Renew? responseRenew =
				await idKeeperApiClient.PostIdKeeperRenew(requestRenew, cancellationToken);

			// 전송 실패(네트워크 오류·타임아웃·5xx)이거나 Ids 필드 자체가 없는 비정상 응답.
			// 어느 쪽도 "서버가 리스를 잃었다"는 근거가 아니므로 — 특히 Ids=null은 빈 목록과
			// 달리 서버의 NotFound 신호가 아니라 응답 이상이므로 — 갱신 시점을 그대로 두고
			// 반환해 다음 RenewLoop 주기에 재시도한다.
			// (RenewLoop는 만료가 가까울수록 주기를 좁혀 재시도 횟수를 확보한다.)
			if (responseRenew is null || responseRenew.Ids is null)
			{
				_logger.LogWarning("Fail to renew: no valid response from IdKeeper API. Will retry.");
				return;
			}

			if (responseRenew.Ids.Count == 0)
			{
				// 서버의 200 + 빈 목록은 RenewResult.NotFound — "이 requester의 리스가 서버에
				// 존재하지 않는다"는 확정 신호다. 서버는 이미 노드 ID를 회수해 다른 프로세스에
				// 재할당할 수 있으므로, 로컬 만료 시각이 남아 있더라도 즉시 발급을 차단한다.
				// 전송 실패(null)와 달리 재시도해도 되살아나지 않으므로 fail-fast 한다.
				StopIssuing("Renew returned no ids: the lease no longer exists on the server.");
				throw new SnowflakeRuntimeException(
					"Renew returned no ids: the lease no longer exists on the server.");
			}

			_logger.LogInformation("ResponseRenew: {ResponseRenew}", responseRenew);

			// 서버는 requester 단위로 보유한 노드 ID 전체를 함께 갱신하므로 개수가 어긋나면
			// 로컬 슬롯 중 일부가 서버에서 사라졌다는 뜻이다. 어느 슬롯이 유효한지 특정할 수
			// 없어 부분 사용은 중복 발급 위험이 있으므로 fail-fast 한다.
			GeneratorSlot[]? slots = Volatile.Read(ref _generatorSlots);
			if (slots is not null && responseRenew.Ids.Count != slots.Length)
			{
				string message = $"Renew id count mismatch: server={responseRenew.Ids.Count}," +
					$" local slots={slots.Length}.";
				StopIssuing(message);
				throw new SnowflakeRuntimeException(message);
			}

			DateTime utcNow = DateTime.UtcNow;
			DateTime expiredAtUtc = responseRenew.Ids.Min(r => r.ExpiredAtUtc).UtcDateTime;

			// 방금 갱신한 리스가 로컬 시계 기준 이미 만료 — 두 시계의 괴리가 리스 길이를
			// 넘어선 것이므로 InitializeAsync와 동일하게 fail-fast 한다.
			// (로컬 시계가 앞선 방향 전용 가드다. 뒤처진 방향은 서버가 ClientUtcNow로 판정하며,
			// Renew에서는 거부하지 않고 경고만 남긴다. 자세한 내용은 InitializeAsync의 주석 참고.)
			if (expiredAtUtc <= utcNow)
			{
				string message = $"Renewed lease is already expired by the local clock" +
					$" (expiredAtUtc={expiredAtUtc:O}, localUtcNow={utcNow:O})." +
					" Check clock synchronization.";
				StopIssuing(message);
				throw new SnowflakeRuntimeException(message);
			}

			Volatile.Write(ref _expiredAtUtcTicks, expiredAtUtc.Ticks);
			Volatile.Write(ref _renewAtUtcTicks, (utcNow + (expiredAtUtc - utcNow) / 2).Ticks);
		}
		catch (OperationCanceledException)
		{
			// Ignore cancellation
		}
		catch (SnowflakeRuntimeException)
		{
			// 의도된 fail-fast 신호 — 아래 catch(Exception)에 삼켜지지 않도록 먼저 다시 던진다.
			// RenewLoopAsync를 거쳐 ExecuteAsync까지 전파되어 호스트 종료로 이어진다.
			throw;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Exception in RenewAsync.");
		}
		finally
		{
			if (acquired)
			{
				_initLock.Release();
			}
		}
	}

	/// <summary>
	/// 리스가 더 이상 유효하지 않다고 판단됐을 때 즉시 발급을 차단한다.
	/// 슬롯을 내리는 것만으로는 이미 슬롯을 캡처한 in-flight 발급을 막지 못하므로, 만료 시각도
	/// 함께 과거로 돌려 AllocateIdCoreAsync의 사후 재검사가 결과를 버리게 한다.
	/// 이후 RemoveAsync는 슬롯이 null이라 조기 반환하지만, 리스가 이미 무효하므로
	/// 서버 반납은 불필요하다.
	/// </summary>
	private void StopIssuing(string reason)
	{
		Volatile.Write(ref _expiredAtUtcTicks, DateTime.UtcNow.Ticks);
		Volatile.Write(ref _renewAtUtcTicks, DateTime.MaxValue.Ticks);
		GeneratorSlot[]? slots = Interlocked.Exchange(ref _generatorSlots, null);

		_logger.LogError(
			"Snowflake id issuance stopped (generators={Count}): {Reason}",
			slots?.Length,
			reason);
	}

	/// <summary>
	/// 다음 RenewLoop 주기까지의 대기 시간을 계산한다. 갱신이 밀린 상태(직전 갱신이 실패해
	/// 갱신 시점이 과거에 머무는 경우 포함)에서는 만료까지 남은 시간에 비례해 간격을 좁혀
	/// 최소 <see cref="MinRenewAttempts"/>회의 재시도를 확보한다. 고정 간격이면
	/// RenewLoopDuration(기본 10분)이 리스 잔여 시간을 통째로 소진해 재시도가 한두 번에
	/// 그칠 수 있다.
	/// </summary>
	private TimeSpan NextLoopDelay()
	{
		TimeSpan delay = _options.RenewLoopDuration;

		DateTime utcNow = DateTime.UtcNow;
		DateTime renewAtUtc = new(Volatile.Read(ref _renewAtUtcTicks), DateTimeKind.Utc);
		if (renewAtUtc > utcNow)
		{
			return delay;
		}

		DateTime expiredAtUtc = new(Volatile.Read(ref _expiredAtUtcTicks), DateTimeKind.Utc);
		TimeSpan remaining = expiredAtUtc - utcNow;
		if (remaining <= TimeSpan.Zero)
		{
			// 이미 만료 — 다음 주기에 만료 분기로 즉시 진입하도록 최소 간격만 대기한다.
			return s_minLoopDelay;
		}

		TimeSpan retryDelay = remaining / MinRenewAttempts;
		if (retryDelay < delay)
		{
			delay = retryDelay;
		}

		return delay < s_minLoopDelay ? s_minLoopDelay : delay;
	}

	private async Task RenewLoopAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				DateTime utcNow = DateTime.UtcNow;
				DateTime expiredAtUtc = new(Volatile.Read(ref _expiredAtUtcTicks), DateTimeKind.Utc);
				DateTime renewAtUtc = new(Volatile.Read(ref _renewAtUtcTicks), DateTimeKind.Utc);
				if (renewAtUtc <= utcNow && utcNow < expiredAtUtc)
				{
					await RenewAsync(cancellationToken);
				}
				else if (expiredAtUtc <= utcNow)
				{
					// 만료 즉시 슬롯을 내려 발급을 차단한다. 만료된 노드 ID는 서버가
					// 다른 프로세스에 재할당할 수 있으므로, 셧다운이 완료될 때까지 발급을
					// 계속하면 ID가 중복될 수 있다.
					StopIssuing($"node id lease expired at {expiredAtUtc:O}");
					throw new SnowflakeRuntimeException(
						$"Snowflake node id was expired. expireAtUtc={expiredAtUtc:O}");
				}

				await Task.Delay(NextLoopDelay(), cancellationToken);
			}
			catch (OperationCanceledException)
			{
				// loop cancel
			}
			catch (SnowflakeRuntimeException)
			{
				// Intentionally fail-fast: bubble up to trigger host shutdown by unhandled exception
				throw;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Exception in RenewLoop");
				// Wrap other exceptions to make fail-fast semantics explicit
				throw new SnowflakeRuntimeException("RenewLoop encountered a critical error.", ex);
			}
		}
	}

	public override void Dispose()
	{
		// StartAsync에서 올린 전역 인스턴스 카운터를 되돌린다. 테스트처럼 StartAsync 없이
		// 생성만 한 경우에는 올린 적이 없으므로 음수가 되지 않게 확인 후 내린다.
		if (_started)
		{
			_started = false;
			Interlocked.Decrement(ref s_runningInstanceCount);
		}

		// 슬롯 락(SemaphoreSlim)은 dispose하지 않는다 — RemoveAsync의 drain이 끝나기
		// 전에 여기가 먼저 실행되면(예: StopAsync 타임아웃) 아직 락 대기 중인
		// TakeFromSlotAsync의 Release()가 ObjectDisposedException을 던질 수 있다.
		// AvailableWaitHandle을 쓰지 않는 한 dispose 자체가 사실상 불필요하다.
		_initLock.Dispose();
		base.Dispose();
	}
}
