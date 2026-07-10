using IdKeeper.Database.Redis.Models;
using StackExchange.Redis;

namespace IdKeeper.Database.Redis.Repositories;

public sealed class RedisBackupScheduleRepository(IConnectionMultiplexer multiplexer)
{
	private static readonly Int32[] AllowedIntervalMinutes = [15, 30, 60, 360, 720, 1440, 2880];

	private IDatabase Db => multiplexer.GetDatabase();

	public async Task<RedisBackupSchedule> GetAsync(CancellationToken cancellationToken = default)
	{
		HashEntry[] entries = await Db.HashGetAllAsync(RedisKeyNames.RedisBackupSchedule.Settings);
		if (entries.Length == 0)
		{
			return new RedisBackupSchedule();
		}

		Dictionary<string, string> fields = entries.ToDictionary(e => (string)e.Name!, e => (string)e.Value!);
		RedisBackupSchedule schedule = new();
		if (fields.TryGetValue("IntervalMinutes", out string? intervalMinutes)
			&& Int32.TryParse(intervalMinutes, out Int32 parsedIntervalMinutes))
		{
			schedule.IntervalMinutes = parsedIntervalMinutes;
		}
		if (fields.TryGetValue("RetentionCount", out string? retentionCount)
			&& Int32.TryParse(retentionCount, out Int32 parsedRetentionCount))
		{
			schedule.RetentionCount = parsedRetentionCount;
		}
		return schedule;
	}

	public async Task SetAsync(
		Int32 intervalMinutes, Int32 retentionCount, CancellationToken cancellationToken = default)
	{
		if (!AllowedIntervalMinutes.Contains(intervalMinutes))
		{
			throw new ArgumentOutOfRangeException(nameof(intervalMinutes),
				intervalMinutes, $"IntervalMinutes must be one of: {string.Join(", ", AllowedIntervalMinutes)}.");
		}
		if (retentionCount is < 1 or > 365)
		{
			throw new ArgumentOutOfRangeException(nameof(retentionCount),
				retentionCount, "RetentionCount must be between 1 and 365.");
		}

		await Db.HashSetAsync(RedisKeyNames.RedisBackupSchedule.Settings,
		[
			new("IntervalMinutes", intervalMinutes),
			new("RetentionCount", retentionCount),
		]);
	}
}
