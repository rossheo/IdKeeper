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
	/// 노드당 1ms 발급 상한을 넘는 개수를 요청하면 내부적으로 다음 밀리초를 기다리므로,
	/// 큰 값은 호출 스레드를 그만큼 점유한다. 대량 발급은
	/// <see cref="NextIdsAsync(Int32, CancellationToken)"/>를 쓰는 편이 낫다.
	/// </summary>
	/// <exception cref="SnowflakeNotReadyException">발급 준비가 되지 않은 경우.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="count"/>가 1 미만이거나 한 번에 발급 가능한 최대치를 넘는 경우.
	/// </exception>
	IReadOnlyList<Int64> NextIds(Int32 count);

	/// <inheritdoc cref="NextIds(Int32)"/>
	Task<IReadOnlyList<Int64>> NextIdsAsync(Int32 count, CancellationToken cancellationToken = default);
}
