namespace IdKeeper.Database.Redis.Extensions;

/// <summary>
/// Redis Hash 필드/ZSET score는 Unix epoch seconds(문자열/숫자)로 통일 저장한다.
/// ISO-8601 문자열 왕복 파싱보다 단순하고 ZSET score와 형식이 일치해 별도 변환 로직이 줄어든다.
/// </summary>
public static class RedisDateTimeExtensions
{
	public static Int64 ToUnixSeconds(this DateTime dateTime) =>
		new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)).ToUnixTimeSeconds();

	public static Int64? ToUnixSeconds(this DateTime? dateTime) =>
		dateTime is null ? null : dateTime.Value.ToUnixSeconds();

	public static DateTime ToUtcDateTime(this Int64 unixSeconds) =>
		DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;

	public static DateTime? ToUtcDateTimeOrNull(this Int64? unixSeconds) =>
		unixSeconds is null ? null : unixSeconds.Value.ToUtcDateTime();

	public static DateTime? ToUtcDateTimeOrNull(this string? value) =>
		string.IsNullOrEmpty(value) ? null : Int64.Parse(value).ToUtcDateTime();

	public static DateTime ToUtcDateTime(this string value) =>
		Int64.Parse(value).ToUtcDateTime();
}
