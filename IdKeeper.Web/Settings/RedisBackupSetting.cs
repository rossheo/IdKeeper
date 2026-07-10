using System.ComponentModel.DataAnnotations;

namespace IdKeeper.Web.Settings;

/// <summary>
/// 백업 파일이 저장되는 경로(배포 인프라 값, PVC 마운트 경로)만 담는다.
/// 백업 주기/보존 개수는 관리자가 화면에서 바로 바꿀 수 있어야 하므로 Redis에 저장한다
/// (<see cref="IdKeeper.Database.Redis.Repositories.RedisBackupScheduleRepository"/> 참조).
/// </summary>
public sealed class RedisBackupSetting
{
	[Required]
	public string RedisBackupDirectory { get; set; } = "/data/redis-backup";
}
