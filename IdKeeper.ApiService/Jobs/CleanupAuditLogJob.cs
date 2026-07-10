using IdKeeper.Database.Redis;
using IdKeeper.Database.Redis.Locking;
using IdKeeper.Database.Redis.Repositories;
using TickerQ.Utilities.Base;

namespace IdKeeper.ApiService.Jobs;

public class CleanupAuditLogJob(
	ILogger<CleanupAuditLogJob> logger,
	AuditLogRepository auditLogRepository,
	RedisLockFactory redisLockFactory)
{
	public static class FunctionNames
	{
		public const string CleanupAuditLog = "CleanupAuditLog";
	}

	[TickerFunction(functionName: FunctionNames.CleanupAuditLog,
		cronExpression: "0 0 0 * * *")]
	public async Task CleanupAuditLog(
		TickerFunctionContext _, CancellationToken cancellationToken)
	{
		logger.LogInformation("{FunctionName} started at: {time}",
			FunctionNames.CleanupAuditLog,
			DateTimeOffset.UtcNow);

		await using RedisLock redLock = await redisLockFactory.TryAcquireAsync(
			RedisKeyNames.Lock.CleanupAuditLogJob, TimeSpan.FromMinutes(5));

		if (!redLock.IsAcquired)
		{
			logger.LogInformation("{FunctionName} skipped: another instance holds the lock.",
				FunctionNames.CleanupAuditLog);
			return;
		}

		try
		{
			DateTime cutoff = DateTime.UtcNow.AddDays(-90);
			Int32 deletedCount = await auditLogRepository.DeleteOlderThanAsync(
				cutoff, cancellationToken: cancellationToken);

			logger.LogInformation(
				"{FunctionName} completed. Deleted {DeletedCount} records (cutoff={Cutoff:yyyy-MM-dd}).",
				FunctionNames.CleanupAuditLog,
				deletedCount, cutoff);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error during {FunctionName}: {message}",
				FunctionNames.CleanupAuditLog,
				ex.Message);
		}
	}
}
