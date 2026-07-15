namespace IdKeeper.Common.Constants;

public sealed record SnowflakeLayout
{
	// 35비트 미만은 wrap-around까지 1년이 채 안 남아 실사용상 위험하다고 판단해 하한으로 둔다
	// (35비트 ≈ 1.09년, 34비트 ≈ 199일). EnsureValid()와 설정 페이지의 입력 하한이 이 값을 공유한다.
	public const Int32 MinTimestampBitCount = 35;

	public Int32 BitCountOfTimestamp { get; init; } = 41;
	public Int32 BitCountOfNodeId { get; init; } = 12;
	public Int32 BitCountOfSequenceId { get; init; } = 10;
	public Int32 BaseDateTimeStartYear { get; init; } = 2026;

	// DateTime은 const 불가이므로 계산 프로퍼티로 노출. Kind=Utc 명시 필수.
	// ToUniversalTime() / new DateTimeOffset(value, Zero) 경로 모두 이 Kind에 의존한다.
	public DateTime BaseDateTime => new(BaseDateTimeStartYear, 1, 1, 0, 0, 0, DateTimeKind.Utc);

	// (1 << 12) - 1 = 4095
	public Int32 MaxNodeIdInclusive => (1 << BitCountOfNodeId) - 1;

	/// <summary>
	/// 타임스탬프 필드가 wrap-around되는 시점(UTC). IdGen의 DefaultTimeSource는 tick 길이를 지정하지
	/// 않으면 항상 1ms이므로(비트 수와 무관), 표현 가능한 최대 ms 값은 (1 &lt;&lt; BitCountOfTimestamp) - 1이다.
	/// 이 시점을 넘기면 타임스탬프 필드가 0으로 되돌아가 과거 ID와 겹치거나 정렬이 깨질 수 있다.
	/// DateTime 표현 범위(연도 9999)를 초과하면 null(사실상 무제한)을 반환한다.
	/// </summary>
	public DateTime? WraparoundDateUtc
	{
		get
		{
			try
			{
				return BaseDateTime.AddMilliseconds((double)((1L << BitCountOfTimestamp) - 1));
			}
			catch (ArgumentOutOfRangeException)
			{
				return null;
			}
		}
	}

	/// <summary>
	/// 63비트 합산 불변식과 각 값의 유효 범위를 검증한다. 값을 저장할 때(SnowflakeLayoutRepository.SetAsync)와
	/// 서비스 기동 시점(Program.cs) 양쪽에서 호출한다.
	/// </summary>
	public void EnsureValid()
	{
		const Int32 ExpectedBitSum = 63;
		const Int32 MinBitCount = 1;
		// IdGen의 IdStructure는 각 비트 수를 byte로 받는다. 63비트 합산 제약상 개별 값은
		// 항상 61 이하이지만, 합산 검증 전에 개별 상한을 먼저 걸어 byte 캐스트 오버플로를 방지한다.
		const Int32 MaxBitCount = 61;
		const Int32 MinStartYear = 2000;

		if (BitCountOfTimestamp is < MinBitCount or > MaxBitCount
			|| BitCountOfNodeId is < MinBitCount or > MaxBitCount
			|| BitCountOfSequenceId is < MinBitCount or > MaxBitCount)
		{
			throw new InvalidOperationException(
				$"Each bit count must be between {MinBitCount} and {MaxBitCount}." +
				$" Current: Timestamp={BitCountOfTimestamp}, NodeId={BitCountOfNodeId}," +
				$" Sequence={BitCountOfSequenceId}");
		}

		if (BitCountOfTimestamp < MinTimestampBitCount)
		{
			throw new InvalidOperationException(
				$"BitCountOfTimestamp must be at least {MinTimestampBitCount}" +
				$" (wrap-around 전까지 최소 유효 기간을 보장하기 위함). Current: {BitCountOfTimestamp}");
		}

		Int32 sum = BitCountOfTimestamp + BitCountOfNodeId + BitCountOfSequenceId;
		if (sum != ExpectedBitSum)
		{
			throw new InvalidOperationException(
				$"BitCountOfTimestamp + BitCountOfNodeId + BitCountOfSequenceId must be {ExpectedBitSum}." +
				$" Current: {sum}");
		}

		Int32 currentUtcYear = DateTime.UtcNow.Year;
		if (BaseDateTimeStartYear < MinStartYear || BaseDateTimeStartYear > currentUtcYear)
		{
			throw new InvalidOperationException(
				$"BaseDateTimeStartYear must be between {MinStartYear} and the current UTC year" +
				$" ({currentUtcYear}). Current: {BaseDateTimeStartYear}");
		}
	}
}

public static class SnowflakeConstant
{
	public static readonly SnowflakeLayout Default = new();
}
