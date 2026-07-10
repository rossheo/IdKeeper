namespace IdKeeper.Database.Redis.Backup;

/// <summary>
/// 백업 파일 하나에 대한 사용자 편집 메타데이터(설명, 보존 정책 제외 여부).
/// </summary>
public sealed class RedisBackupFileMetadata
{
	public string Description { get; set; } = string.Empty;
	public bool Keep { get; set; }
}
