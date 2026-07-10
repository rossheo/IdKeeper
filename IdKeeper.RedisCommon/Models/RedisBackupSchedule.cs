namespace IdKeeper.Database.Redis.Models;

public class RedisBackupSchedule
{
	public Int32 IntervalMinutes { get; set; } = 1440;
	public Int32 RetentionCount { get; set; } = 7;
}
