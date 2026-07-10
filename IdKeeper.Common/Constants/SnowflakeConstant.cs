namespace IdKeeper.Common.Constants;

public static class SnowflakeConstant
{
	// DateTime은 const 불가이므로 static readonly 사용. Kind=Utc 명시 필수.
	// ToUniversalTime() / new DateTimeOffset(value, Zero) 경로 모두 이 Kind에 의존한다.
	public static readonly DateTime BaseDateTime =
		new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

	public const Int32 BitCountOfTimestamp = 41;
	public const Int32 BitCountOfNodeId = 12;
	public const Int32 BitCountOfSequenceId = 10;

	// (1 << 12) - 1 = 4095
	public const Int32 MaxNodeIdInclusive = (1 << BitCountOfNodeId) - 1;

	/// <summary>
	/// 63비트 합산 불변식 검증. Program.cs 기동 시점에 명시적으로 호출한다.
	/// const 인라이닝으로 static 생성자가 트리거되지 않는 경우를 커버한다.
	/// </summary>
	public static void EnsureValid()
	{
		const Int32 Expected = 63;
		Int32 sum = BitCountOfTimestamp + BitCountOfNodeId + BitCountOfSequenceId;
		if (sum != Expected)
			throw new InvalidOperationException(
				$"BitCountOfTimestamp + BitCountOfNodeId + BitCountOfSequenceId must be {Expected}." +
				$" Current: {sum}");
	}
}
