using IdKeeper.Database.Redis;
using IdKeeper.Database.Redis.Extensions;
using IdKeeper.Database.Redis.Locking;
using IdKeeper.Database.Redis.Scripts;
using StackExchange.Redis;
using TickerQ.Utilities.Base;

namespace IdKeeper.ApiService.Jobs;

public class CleanupExpiredJob(
	ILogger<CleanupExpiredJob> logger,
	IConnectionMultiplexer multiplexer,
	RedisLockFactory redisLockFactory,
	LuaScriptLoader scripts)
{
	public static class FunctionNames
	{
		public const string CleanupExpired = "CleanupExpired";
	}

	private const Int32 BatchSize = 200;

	[TickerFunction(functionName: FunctionNames.CleanupExpired,
		cronExpression: "0 */10 * * * *")]
	public async Task CleanupExpired(
		TickerFunctionContext _, CancellationToken cancellationToken)
	{
		logger.LogInformation("{FunctionName} started at: {time}",
			FunctionNames.CleanupExpired,
			DateTimeOffset.UtcNow);

		// 다중 ApiService 레플리카가 동시에 이 잡을 실행하는 것을 방지한다.
		// 이미 다른 인스턴스가 실행 중이면 이번 회차는 스킵한다.
		await using RedisLock redLock = await redisLockFactory.TryAcquireAsync(
			RedisKeyNames.Lock.CleanupExpiredJob, TimeSpan.FromMinutes(5));

		if (!redLock.IsAcquired)
		{
			logger.LogInformation("{FunctionName} skipped: another instance holds the lock.",
				FunctionNames.CleanupExpired);
			return;
		}

		try
		{
			IDatabase db = multiplexer.GetDatabase();
			Int64 cutoffUnix = DateTime.UtcNow.ToUnixSeconds();
			Int32 totalDeleted = 0;

			while (true)
			{
				RedisValue[] expiredIds = await db.SortedSetRangeByScoreAsync(
					RedisKeyNames.AllocatedId.ExpiryIndex,
					double.NegativeInfinity, cutoffUnix, take: BatchSize);

				if (expiredIds.Length == 0)
				{
					break;
				}

				RedisKey[] keys = [RedisKeyNames.AllocatedId.Bitmap, RedisKeyNames.AllocatedId.ExpiryIndex];
				RedisValue[] values =
				[
					"IdKeeper/AllocatedId/{AllocatedId}/",
					"IdKeeper/AllocatedId/{AllocatedId}/ByRequester/",
					.. expiredIds,
				];

				await db.ScriptEvaluateAsync(
					scripts.Load("CleanupExpiredBatch"), keys, values).WaitAsync(cancellationToken);

				totalDeleted += expiredIds.Length;
				if (expiredIds.Length < BatchSize)
				{
					break;
				}
			}

			logger.LogInformation(
				"{FunctionName} completed. Deleted {DeletedCount} expired records.",
				FunctionNames.CleanupExpired,
				totalDeleted);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error during {FunctionName}: {message}",
				FunctionNames.CleanupExpired,
				ex.Message);
		}
	}
}
