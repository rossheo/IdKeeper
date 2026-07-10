using System.Globalization;

namespace IdKeeper.Database.Redis.Backup;

/// <summary>
/// 디스크에 저장되는 백업 파일(NDJSON)의 명명/정렬/보존 규칙을 한 곳에 모은다.
/// 파일명에 박힌 "yyyyMMdd-HHmmss"(0-패딩)의 사전식 정렬이 시간순 정렬과 동일하다는 점을
/// 이용한다 — Linux 컨테이너(overlayfs)에서는 FileInfo.CreationTimeUtc가 신뢰할 수 없어
/// 정렬 기준으로 쓰지 않는다. 주기적 백업 잡과 수동 백업 생성/목록 UI가 이 클래스를 공유해
/// 정렬·보존 로직이 여러 곳에서 따로 재현되지 않게 한다.
/// </summary>
public static class RedisBackupFileCatalog
{
	public const string FilePrefix = "idkeeper-redis-backup-";
	public const string FileExtension = ".ndjson";

	private const string TimestampFormat = "yyyyMMdd-HHmmss";

	public static string BuildFileName(DateTime utcNow) =>
		$"{FilePrefix}{utcNow.ToString(TimestampFormat, CultureInfo.InvariantCulture)}{FileExtension}";

	public static IReadOnlyList<FileInfo> ListSortedDescending(string directory)
	{
		if (!Directory.Exists(directory))
		{
			return [];
		}

		return [.. new DirectoryInfo(directory)
			.GetFiles($"{FilePrefix}*{FileExtension}")
			.OrderByDescending(f => f.Name, StringComparer.Ordinal)];
	}

	public static bool TryParseTimestampUtc(string fileName, out DateTime utc)
	{
		string trimmed = fileName;
		if (trimmed.StartsWith(FilePrefix, StringComparison.Ordinal))
		{
			trimmed = trimmed[FilePrefix.Length..];
		}
		if (trimmed.EndsWith(FileExtension, StringComparison.Ordinal))
		{
			trimmed = trimmed[..^FileExtension.Length];
		}

		return DateTime.TryParseExact(
			trimmed, TimestampFormat, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out utc);
	}

	/// <summary>
	/// 최신 retentionCount개만 남기고 나머지 백업 파일을 삭제한다. keepFileNames에 담긴
	/// 파일은 개수·순서와 무관하게 삭제 대상에서 완전히 제외된다("삭제 정책에서 제외").
	/// </summary>
	public static void PruneRetained(
		string directory, Int32 retentionCount, IReadOnlySet<string> keepFileNames)
	{
		IEnumerable<FileInfo> prunable =
			ListSortedDescending(directory).Where(f => !keepFileNames.Contains(f.Name));
		foreach (FileInfo file in prunable.Skip(retentionCount))
		{
			file.Delete();
		}
	}
}
