namespace IdKeeper.Client;

/// <summary>발급 요청에 적용되는 한계값. 호출부의 입력 검증에 쓴다.</summary>
public static class SnowflakeIdLimits
{
	/// <summary>
	/// <see cref="ISnowflakeIdGenerator.NextIdsAsync(Int32, System.Threading.CancellationToken)"/>
	/// 한 번에 요청할 수 있는 최대 개수.
	///
	/// 상한을 두는 이유: 요청 개수가 크면 거대 배열을 잡고, 노드당 1ms 발급 상한 때문에 다음
	/// 밀리초를 기다리며 슬롯 락을 오래 점유한다.
	/// </summary>
	public const Int32 MaxAllocateCount = 10_000;
}
