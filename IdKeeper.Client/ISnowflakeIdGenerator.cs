namespace IdKeeper.Client;

/// <summary>
/// SnowflakeId 발급기. 소비자가 사용하는 표면이다.
///
/// IdKeeper 서버에서 임대받은 노드 Id로 프로세스 내에서 직접 발급하므로 발급마다 네트워크
/// 호출이 발생하지 않는다. 임대 획득·갱신·반납은 백그라운드에서 자동으로 처리된다.
/// </summary>
public interface ISnowflakeIdGenerator
{
	/// <summary>
	/// 임대가 유효하고 발급 준비가 끝났는지. 기동 직후 임대를 받기 전에는 false다.
	/// </summary>
	bool IsReady { get; }

	/// <summary>
	/// ID 하나를 발급한다.
	/// </summary>
	/// <exception cref="SnowflakeNotReadyException">
	/// 아직 임대를 받지 못했거나, 임대가 만료되어 발급이 차단된 경우.
	/// </exception>
	Int64 NextId();

	/// <summary>
	/// ID를 <paramref name="count"/>개 발급한다. 반환 목록은 오름차순 정렬을 보장한다.
	///
	/// 동기 버전을 제공하지 않는 이유: 내부적으로 슬롯 락을 비동기로 대기하므로 동기 래퍼는
	/// sync-over-async가 되어, SynchronizationContext가 있는 소비자(WPF·WinForms 등)에서
	/// 경합 시 데드락이 날 수 있다. 적은 개수가 필요하면 <see cref="NextId"/>를 여러 번
	/// 호출하면 되고(슬롯에 분산되어 오히려 효율적이다), 대량이면 이 메서드를 await 한다.
	/// </summary>
	/// <exception cref="SnowflakeNotReadyException">발급 준비가 되지 않은 경우.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="count"/>가 1 미만이거나
	/// <see cref="SnowflakeIdLimits.MaxAllocateCount"/>를 넘는 경우.
	/// </exception>
	Task<IReadOnlyList<Int64>> NextIdsAsync(Int32 count, CancellationToken cancellationToken = default);
}
