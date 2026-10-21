namespace IdKeeper.Common.Constants;

public static class DateTimeConstant
{
	/// <summary>
	/// 관리 화면의 표시 기준 시간대. 서버(컨테이너)의 TZ 설정과 무관하게 항상 KST로 표시한다.
	/// </summary>
	public static readonly TimeZoneInfo KstZone =
		TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul");

	/// <summary>
	/// UTC DateTime을 KST로 변환한다. Kind가 무엇이든 UTC 값으로 간주한다. (반환 Kind=Unspecified)
	/// </summary>
	public static DateTime ToKst(DateTime utcDateTime) =>
		TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc), KstZone);

	public static DateTimeOffset ToKst(DateTimeOffset dateTimeOffset) =>
		TimeZoneInfo.ConvertTime(dateTimeOffset, KstZone);

	/// <summary>
	/// KST 일시를 UTC로 변환한다. Kind가 무엇이든 KST 값으로 간주한다. (반환 Kind=Utc)
	/// </summary>
	public static DateTime FromKst(DateTime kstDateTime) =>
		TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(kstDateTime, DateTimeKind.Unspecified), KstZone);

	public static string ConvertTitle(string title, bool useKst) =>
		useKst ? $"{title} (KST)" : $"{title} (UTC)";

	public static string ConvertDateTime(DateTime dateTime, bool useKst) =>
		ConvertDateTime((DateTime?)dateTime, useKst);

	public static string ConvertDateTime(DateTime? dateTime, bool useKst)
	{
		if (dateTime is null)
		{
			return "-";
		}

		DateTime utc = DateTime.SpecifyKind(dateTime.Value, DateTimeKind.Utc);
		DateTime convertedDateTime = useKst ? ToKst(utc) : utc;
		return $"{convertedDateTime:yyyy-MM-dd HH:mm:ss}";
	}

	public static string GetElapsedTime(DateTime? dateTime) =>
		dateTime is null ? "0s" : FormatDuration(DateTime.UtcNow - AsUtc(dateTime.Value), "-");

	public static string GetRemainTime(DateTime? dateTime) =>
		dateTime is null ? "0s" : FormatDuration(AsUtc(dateTime.Value) - DateTime.UtcNow, string.Empty);

	private static DateTime AsUtc(DateTime dateTime) => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);

	/// <summary>
	/// 가장 큰 단위 하나(d/h/m/s)로 표시한다. 0 이하이면 부호 없이 "0s"를 반환한다.
	/// </summary>
	private static string FormatDuration(TimeSpan duration, string prefix)
	{
		if (duration.Ticks <= 0)
		{
			return "0s";
		}

		if (duration.TotalDays >= 1)
		{
			return $"{prefix}{(Int64)duration.TotalDays}d";
		}

		if (duration.TotalHours >= 1)
		{
			return $"{prefix}{duration.Hours}h";
		}

		if (duration.TotalMinutes >= 1)
		{
			return $"{prefix}{duration.Minutes}m";
		}

		return $"{prefix}{duration.Seconds}s";
	}
}
