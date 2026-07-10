namespace IdKeeper.Common.Constants;

public static class DateTimeConstant
{
	public static readonly TimeZoneInfo KstZone =
		TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul");

	/// <summary>
	/// 현재 KST 일시를 반환한다. (Kind=Unspecified)
	/// </summary>
	public static DateTime KstNow(TimeProvider timeProvider) =>
		TimeZoneInfo.ConvertTimeFromUtc(timeProvider.GetUtcNow().UtcDateTime, KstZone);

	/// <inheritdoc cref="KstNow(TimeProvider)"/>
	public static DateTime KstNow() => KstNow(TimeProvider.System);

	/// <summary>
	/// KST 달력상의 오늘 날짜를 Kind=Utc 자정으로 반환한다.
	/// </summary>
	public static DateTime KstToday(TimeProvider timeProvider) =>
		DateTime.SpecifyKind(KstNow(timeProvider).Date, DateTimeKind.Utc);

	/// <inheritdoc cref="KstToday(TimeProvider)"/>
	public static DateTime KstToday() => KstToday(TimeProvider.System);

	/// <summary>
	/// UTC DateTime을 KST로 변환한다. (Kind=Unspecified)
	/// </summary>
	public static DateTime KstFrom(DateTime utcDateTime) =>
		TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, KstZone);

	public static string ConvertTitle(string title, bool useLocalTime)
	{
		if (useLocalTime)
		{
			return $"{title} (Local)";
		}

		return $"{title} (UTC)";
	}

	public static string ConvertDateTime(DateTime dateTime, bool useLocalTime) =>
		ConvertDateTime((DateTime?)dateTime, useLocalTime);

	public static string ConvertDateTime(DateTime? dateTime, bool useLocalTime)
	{
		if (dateTime is null)
		{
			return "-";
		}

		DateTime utc = DateTime.SpecifyKind(dateTime.Value, DateTimeKind.Utc);
		DateTime convertedDateTime = useLocalTime ? utc.ToLocalTime() : utc;
		return $"{convertedDateTime:yyyy-MM-dd HH:mm:ss}";
	}

	public static string GetElapsedTime(DateTime? dateTime)
	{
		if (dateTime is null)
		{
			return "0s";
		}

		DateTime utcTarget = DateTime.SpecifyKind(dateTime.Value, DateTimeKind.Utc);
		Int64 ticksElapsed = (DateTime.UtcNow - utcTarget).Ticks;
		if (ticksElapsed <= 0)
		{
			return "0s";
		}

		Int64 totalSeconds = ticksElapsed / TimeSpan.TicksPerSecond;

		const Int64 SecondsPerMinute = 60;
		const Int64 SecondsPerHour = 60 * SecondsPerMinute;
		const Int64 SecondsPerDay = 24 * SecondsPerHour;

		Int64 days = totalSeconds / SecondsPerDay;
		totalSeconds %= SecondsPerDay;

		Int64 hours = totalSeconds / SecondsPerHour;
		totalSeconds %= SecondsPerHour;

		Int64 minutes = totalSeconds / SecondsPerMinute;
		Int64 seconds = totalSeconds % SecondsPerMinute;

		if (days >= 1)
		{
			return $"-{days}d";
		}

		if (hours >= 1)
		{
			return $"-{hours}h";
		}

		if (minutes >= 1)
		{
			return $"-{minutes}m";
		}

		return $"-{seconds}s";
	}

	public static string GetRemainTime(DateTime? dateTime)
	{
		if (dateTime is null)
		{
			return "0s";
		}

		DateTime utcTarget = DateTime.SpecifyKind(dateTime.Value, DateTimeKind.Utc);
		Int64 ticksRemain = (utcTarget - DateTime.UtcNow).Ticks;
		if (ticksRemain <= 0)
		{
			return "0s";
		}

		Int64 totalSeconds = ticksRemain / TimeSpan.TicksPerSecond;

		const Int64 SecondsPerMinute = 60;
		const Int64 SecondsPerHour = 60 * SecondsPerMinute;
		const Int64 SecondsPerDay = 24 * SecondsPerHour;

		Int64 days = totalSeconds / SecondsPerDay;
		totalSeconds %= SecondsPerDay;

		Int64 hours = totalSeconds / SecondsPerHour;
		totalSeconds %= SecondsPerHour;

		Int64 minutes = totalSeconds / SecondsPerMinute;
		Int64 seconds = totalSeconds % SecondsPerMinute;

		if (days >= 1)
		{
			return $"{days}d";
		}

		if (hours >= 1)
		{
			return $"{hours}h";
		}

		if (minutes >= 1)
		{
			return $"{minutes}m";
		}

		return $"{seconds}s";
	}
}