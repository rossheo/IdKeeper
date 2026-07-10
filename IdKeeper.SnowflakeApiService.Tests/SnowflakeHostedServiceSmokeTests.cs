using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using IdGen;
using Xunit;
using IdKeeper.SnowflakeApiService.HostedServices;
using IdKeeper.SnowflakeApiService.HttpClients;
using IdKeeper.SnowflakeApiService.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdKeeper.SnowflakeApiService.Tests;

public sealed class SnowflakeHostedServiceSmokeTests : IDisposable
{
	// IdStructure(41, 10, 12): nodeId = (id >> 12) & 0x3FF
	private const Int32 NodeIdShift = 12;
	private const Int64 NodeIdMask = 0x3FF;

	private static readonly FieldInfo SlotField =
		typeof(SnowflakeHostedService)
			.GetField("_generatorSlots", BindingFlags.NonPublic | BindingFlags.Instance)!;

	private static readonly FieldInfo AllocCountField =
		typeof(SnowflakeHostedService)
			.GetField("_allocatingCount", BindingFlags.NonPublic | BindingFlags.Instance)!;

	private static readonly Type SlotType =
		typeof(SnowflakeHostedService)
			.GetNestedType("GeneratorSlot", BindingFlags.NonPublic)!;

	private static readonly ConstructorInfo SlotCtor = SlotType.GetConstructors()[0];

	private static readonly PropertyInfo SlotLockProperty = SlotType.GetProperty("Lock")!;

	private static readonly IdGeneratorOptions Options = new(
		new IdStructure(41, 10, 12),
		new DefaultTimeSource(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
		SequenceOverflowStrategy.SpinWait);

	private readonly SnowflakeHostedService _sut;
	private readonly ServiceProvider _serviceProvider;
	private readonly FakeHttpHandler _fakeHandler = new();

	public SnowflakeHostedServiceSmokeTests()
	{
		SnowflakeSetting setting = new() { IdKeeperApiKey = "test-key", GeneratorCount = 3 };
		HttpClient httpClient = new(_fakeHandler) { BaseAddress = new Uri("http://test/") };
		IdKeeperApiClient apiClient = new(
			NullLogger<IdKeeperApiClient>.Instance, httpClient, setting);

		ServiceCollection services = new();
		services.AddSingleton(apiClient);
		_serviceProvider = services.BuildServiceProvider();

		_sut = new SnowflakeHostedService(
			NullLogger<SnowflakeHostedService>.Instance,
			new TestScopeFactory(_serviceProvider),
			setting,
			new TestHostLifetime());
	}

	private void SetSlots(Int32 count = 3)
	{
		Array arr = Array.CreateInstance(SlotType, count);
		for (Int32 i = 0; i < count; i++)
			arr.SetValue(SlotCtor.Invoke([new IdGenerator(i, Options)]), i);
		SlotField.SetValue(_sut, arr);
	}

	/// <summary>
	/// 다수 스레드의 동시 발급 결과에 중복 ID가 없어야 한다.
	/// </summary>
	[Fact]
	public async Task ConcurrentAlloc_AllIdsDistinct()
	{
		SetSlots(3);

		const Int32 TaskCount = 20;
		const Int32 PerTask = 500;

		IEnumerable<Int64>[] results = await Task.WhenAll(
			Enumerable.Range(0, TaskCount)
				.Select(_ => Task.Run(
					() => _sut.AllocateIdAsync(PerTask, CancellationToken.None))));

		Int64[] all = results.SelectMany(r => r).ToArray();

		Assert.Equal(TaskCount * PerTask, all.Length);
		Assert.Equal(all.Length, all.Distinct().Count());
	}

	/// <summary>
	/// 소량 요청이 반복될 때 라운드로빈으로 모든 슬롯이 사용되어야 한다.
	/// </summary>
	[Fact]
	public async Task RoundRobin_DistributesAcrossAllSlots()
	{
		SetSlots(3);

		List<Int64> ids = [];
		for (Int32 i = 0; i < 30; i++)
		{
			IEnumerable<Int64> batch = await _sut.AllocateIdAsync(1, CancellationToken.None);
			ids.AddRange(batch);
		}

		HashSet<Int64> usedNodeIds = ids
			.Select(id => (id >> NodeIdShift) & NodeIdMask)
			.ToHashSet();

		Assert.Equal(3, usedNodeIds.Count);
	}

	/// <summary>
	/// genCount>1 대용량 배치: 3개 슬롯이 병렬로 청크를 처리하고
	/// 결과가 중복 없이 정렬되어 반환되어야 한다.
	/// IdStructure(41,10,12) → MaxSequenceIds=4096.
	/// count=10000 → genCount=min(3, ceil(10000/4096))=3.
	/// </summary>
	[Fact]
	public async Task LargeBatch_AllSlotsUsedAndResultSortedDistinct()
	{
		SetSlots(3);

		const Int32 Count = 10_000;

		IEnumerable<Int64> ids = await _sut.AllocateIdAsync(Count, CancellationToken.None);
		Int64[] all = ids.ToArray();

		Assert.Equal(Count, all.Length);
		Assert.Equal(Count, all.Distinct().Count());
		// 서로 다른 nodeId를 가진 Generator가 혼재하므로 Sort 없이는 오름차순이 보장되지 않음
		Assert.Equal(all, all.OrderBy(x => x).ToArray());

		// 세 슬롯(nodeId 0·1·2)이 모두 사용됐는지 확인
		HashSet<Int64> usedNodeIds = all
			.Select(id => (id >> NodeIdShift) & NodeIdMask)
			.ToHashSet();
		Assert.Equal(3, usedNodeIds.Count);
	}

	/// <summary>
	/// RemoveAsync는 진행 중인 AllocateIdAsync가 완료된 후에만 서버를 호출해야 한다.
	///
	/// 설계:
	///   - FakeHttpHandler의 ResponseGate(초기 count=0)가 서버 응답 직전 대기.
	///     OnRemoveCalling은 ResponseGate 대기 직전에 호출되므로
	///     "Remove가 서버에 도달했는가"를 독립적으로 기록할 수 있다.
	///   - 드레인 있음: StopAsync는 drainTcs에서 대기 → 50ms 후 removeCalled=false → 단언 통과.
	///   - 드레인 없음: StopAsync가 즉시 HTTP 호출 → removeCalled=true → 단언 실패(falsifiable).
	///     HttpClient 내부에 yield가 있더라도 50ms 내에 OnRemoveCalling이 실행되므로
	///     타이밍 기반 불확실성은 해소된다.
	/// </summary>
	[Fact]
	public async Task Drain_ServerCalledOnlyAfterInflightAllocCompletes()
	{
		SetSlots(3);

		// _nextSlot=0 → Add(1) - 1 = 0 % 3 = 0 → slots[0] 사용 (결정적)
		Array slotsArr = (Array)SlotField.GetValue(_sut)!;
		SemaphoreSlim slot0Lock =
			(SemaphoreSlim)SlotLockProperty.GetValue(slotsArr.GetValue(0)!)!;

		// 테스트 스레드가 slots[0].Lock 선점 → alloc을 in-flight 상태로 유지
		await slot0Lock.WaitAsync();

		// alloc 시작: Increment(count=1) 후 slot0Lock 대기 (동기 구간에서 반환됨)
		Task<IReadOnlyList<Int64>> allocTask = _sut.AllocateIdAsync(1, CancellationToken.None);

		// _allocatingCount == 1 대기 (slots 읽기 완료 보장)
		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
		while ((Int32)AllocCountField.GetValue(_sut)! != 1)
			await Task.Delay(1, cts.Token);

		// ResponseGate(0,1): Remove가 서버 응답을 반환하기 전에 대기
		// OnRemoveCalling: ResponseGate 대기 직전에 호출 → 서버 도달 시점을 기록
		SemaphoreSlim responseGate = new(0, 1);
		bool removeCalled = false;
		_fakeHandler.ResponseGate = responseGate;
		_fakeHandler.OnRemoveCalling = () => Volatile.Write(ref removeCalled, true);

		// StopAsync 시작
		// - 드레인 있음: drainTcs에서 대기 → HTTP 미도달 → stopTask 반환
		// - 드레인 없음: HTTP 즉시 호출 → removeCalled=true → ResponseGate 대기 → stopTask 반환
		Task stopTask = _sut.StopAsync(CancellationToken.None);

		// 50ms 대기: HttpClient 내부 yield가 있어도 이 시간 안에 OnRemoveCalling이 실행됨.
		// 드레인 있음 → RemoveAsync는 drainTcs 대기 중 → removeCalled=false.
		// 드레인 없음 → RemoveAsync는 이미 서버를 호출함 → removeCalled=true.
		await Task.Delay(50);
		bool removedBeforeAllocDone = Volatile.Read(ref removeCalled);

		// alloc 완료 허용 & HTTP 응답 허용
		slot0Lock.Release();
		responseGate.Release();

		using CancellationTokenSource cts2 = new(TimeSpan.FromSeconds(5));
		await Task.WhenAll(stopTask, allocTask).WaitAsync(cts2.Token);

		// 핵심: 드레인이 없으면 여기서 실패한다
		Assert.False(removedBeforeAllocDone,
			"RemoveAsync가 in-flight AllocateIdAsync 완료 전에 서버를 호출했다 (드레인 누락).");
		Assert.True(removeCalled, "RemoveAsync가 서버를 호출하지 않았다.");
		Assert.Single(await allocTask);

		// Stop 완료 후 새 발급은 빈 결과
		IEnumerable<Int64> afterStop = await _sut.AllocateIdAsync(1, CancellationToken.None);
		Assert.Empty(afterStop);
	}

	public void Dispose() => _serviceProvider.Dispose();
}

// ── 테스트 전용 협력 객체 ──────────────────────────────────────────────────────

sealed class FakeHttpHandler : HttpMessageHandler
{
	public Action? OnRemoveCalling { get; set; }
	public SemaphoreSlim? ResponseGate { get; set; }

	protected override async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request, CancellationToken cancellationToken)
	{
		if (request.RequestUri!.AbsolutePath.EndsWith("Remove"))
		{
			OnRemoveCalling?.Invoke();
			if (ResponseGate is not null)
				await ResponseGate.WaitAsync(cancellationToken);
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(
					new IdKeeperApiClient.ResponseV1Remove([0, 1, 2]))
			};
		}
		return new HttpResponseMessage(HttpStatusCode.NotFound);
	}
}

file sealed class TestScopeFactory(IServiceProvider provider) : IServiceScopeFactory
{
	public IServiceScope CreateScope() => new TestScope(provider);
}

file sealed class TestScope(IServiceProvider provider) : IServiceScope, IAsyncDisposable
{
	public IServiceProvider ServiceProvider => provider;
	public void Dispose() { }
	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

file sealed class TestHostLifetime : IHostApplicationLifetime
{
	public CancellationToken ApplicationStarted => CancellationToken.None;
	public CancellationToken ApplicationStopping => CancellationToken.None;
	public CancellationToken ApplicationStopped => CancellationToken.None;
	public void StopApplication() { }
}
