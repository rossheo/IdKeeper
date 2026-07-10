using IdGen;
using IdKeeper.Common.Constants;
using IdKeeper.SnowflakeApiService.Exceptions;
using IdKeeper.SnowflakeApiService.HttpClients;
using IdKeeper.SnowflakeApiService.Settings;
using static IdKeeper.SnowflakeApiService.HttpClients.IdKeeperApiClient;

namespace IdKeeper.SnowflakeApiService.HostedServices;

public class SnowflakeHostedService : BackgroundService
{
	private sealed class GeneratorSlot(IdGenerator generator)
	{
		public IdGenerator Generator { get; } = generator;
		public SemaphoreSlim Lock { get; } = new(1, 1);
	}

	// 한 번에 발급 가능한 최대 ID 수. 컨트롤러 DTO(SnowflakeIdRequestV1Alloc)의
	// Range 상한과 공유한다.
	public const Int32 MaxAllocateCount = 10_000;

	private readonly ILogger _logger;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly SnowflakeSetting _snowflakeSetting;
	private readonly IHostApplicationLifetime _hostLifetime;

	// _initLock: InitializeAsync / RenewAsync / RemoveAsync 만 취득.
	// AllocateIdAsync는 취득하지 않음 — Volatile.Read + 슬롯별 Lock으로 동작.
	private readonly SemaphoreSlim _initLock = new(1, 1);
	private GeneratorSlot[]? _generatorSlots;
	private Int32 _nextSlot;
	private Int32 _allocatingCount;
	private TaskCompletionSource? _drainTcs;
	private DateTime _renewAtUtc = DateTime.MaxValue;
	// AllocateIdCoreAsync(임의 스레드)에서도 읽으므로 torn read 방지를 위해 ticks(Int64)로 보관.
	private Int64 _expiredAtUtcTicks = DateTime.MaxValue.Ticks;

	public SnowflakeHostedService(
		ILogger<SnowflakeHostedService> logger,
		IServiceScopeFactory scopeFactory,
		SnowflakeSetting snowflakeSetting,
		IHostApplicationLifetime hostLifetime)
	{
		_logger = logger;
		_scopeFactory = scopeFactory;
		_snowflakeSetting = snowflakeSetting;
		_hostLifetime = hostLifetime;
	}

	public Task<bool> IsReadyAsync(CancellationToken _)
		=> Task.FromResult(Volatile.Read(ref _generatorSlots) is not null);

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

	private async Task InitializeAsync(CancellationToken cancellationToken)
	{
		const Int32 RetryDelayMs = 3000;
		Int32 attempt = 0;

		while (!cancellationToken.IsCancellationRequested)
		{
			attempt++;
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

				Int32 requestCount = _snowflakeSetting.GeneratorCount;
				RequestV1Alloc requestAlloc = new(Count: requestCount, MachineConstant.UniqueProcessId);
				ResponseV1Alloc? responseAlloc =
					await idKeeperApiClient.PostIdKeeperAlloc(requestAlloc, cancellationToken);

				if (responseAlloc is null || responseAlloc.Ids.Count == 0)
				{
					_logger.LogWarning(
						"Failed to alloc node id (attempt {Attempt}), retrying in {DelayMs}ms",
						attempt,
						RetryDelayMs);
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

					Volatile.Write(
						ref _expiredAtUtcTicks,
						responseAlloc.Ids.Min(r => r.ExpiredAtUtc).UtcDateTime.Ticks);
					// 의도된 동작: _renewAtUtc를 현재 시각으로 설정해 첫 RenewLoop 진입 시
					// 즉시 Renew를 1회 수행한다. 시작 직후 갱신 경로가 정상인지 조기에
					// 검증하고, 이후 갱신 시점은 RenewAsync가 만료 시각의 절반 지점으로
					// 재계산한다.
					_renewAtUtc = DateTime.UtcNow;

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
				await Task.Delay(RetryDelayMs, cancellationToken);
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
			_renewAtUtc = DateTime.MaxValue;

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

			RequestV1Remove requestRemove = new(MachineConstant.UniqueProcessId);
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

			RequestV1Renew requestRenew = new(MachineConstant.UniqueProcessId);
			ResponseV1Renew? responseRenew =
				await idKeeperApiClient.PostIdKeeperRenew(requestRenew, cancellationToken);
			if (responseRenew is null || responseRenew.Ids.Count == 0)
			{
				// 의도된 동작: 실패 시 _renewAtUtc를 갱신하지 않고 반환한다.
				// _renewAtUtc가 과거에 머물러 다음 RenewLoop 주기마다 재시도하게 되며,
				// 별도 백오프 없이 만료 전까지 RenewLoopDuration 간격으로 계속 시도한다.
				_logger.LogWarning("Fail to renew. Check error logs.");
				return;
			}

			_logger.LogInformation("ResponseRenew: {ResponseRenew}", responseRenew);

			DateTime utcNow = DateTime.UtcNow;
			DateTime expiredAtUtc = responseRenew.Ids.Min(r => r.ExpiredAtUtc).UtcDateTime;
			Volatile.Write(ref _expiredAtUtcTicks, expiredAtUtc.Ticks);
			_renewAtUtc = utcNow + (expiredAtUtc - utcNow) / 2;
		}
		catch (OperationCanceledException)
		{
			// Ignore cancellation
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

	private async Task RenewLoopAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				DateTime utcNow = DateTime.UtcNow;
				DateTime expiredAtUtc = new(Volatile.Read(ref _expiredAtUtcTicks), DateTimeKind.Utc);
				if (_renewAtUtc <= utcNow && utcNow < expiredAtUtc)
				{
					await RenewAsync(cancellationToken);
				}
				else if (expiredAtUtc <= utcNow)
				{
					// 만료 즉시 슬롯을 내려 발급을 차단한다. 만료된 노드 ID는 서버가
					// 다른 프로세스에 재할당할 수 있으므로, 셧다운이 완료될 때까지 발급을
					// 계속하면 ID가 중복될 수 있다. 이후 RemoveAsync는 슬롯이 null이라
					// 조기 반환하지만, 리스가 이미 만료됐으므로 서버 반납은 불필요하다.
					GeneratorSlot[]? slots = Interlocked.Exchange(ref _generatorSlots, null);
					_logger.LogError(
						"Snowflake node id was expired. generators={Count}, expireAtUtc={ExpireAtUtc}",
						slots?.Length,
						expiredAtUtc);
					throw new SnowflakeRuntimeException(
						$"Snowflake node id was expired. expireAtUtc={expiredAtUtc}");
				}

				await Task.Delay(_snowflakeSetting.RenewLoopDuration, cancellationToken);
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
		// 슬롯 락(SemaphoreSlim)은 dispose하지 않는다 — RemoveAsync의 drain이 끝나기
		// 전에 여기가 먼저 실행되면(예: StopAsync 타임아웃) 아직 락 대기 중인
		// TakeFromSlotAsync의 Release()가 ObjectDisposedException을 던질 수 있다.
		// AvailableWaitHandle을 쓰지 않는 한 dispose 자체가 사실상 불필요하다.
		_initLock.Dispose();
		base.Dispose();
	}
}
