using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace IdKeeper.Database.Redis.Startup;

// 기동 시점의 Redis 접근은 Redis 파드가 재생성되는 중이면 잠깐 실패할 수 있다.
// 한 번 실패했다고 프로세스를 종료시키면 컨테이너가 재시작을 반복하므로,
// 연결/타임아웃 오류에 한해 일정 간격으로 몇 번 더 시도한다.
public static class RedisStartupRetry
{
	public const Int32 MaxRetries = 3;

	public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(20);

	public static async Task<T> ExecuteAsync<T>(
		Func<Task<T>> operation,
		ILogger logger,
		string operationName,
		CancellationToken cancellationToken = default)
	{
		for (Int32 retry = 0; ; retry++)
		{
			try
			{
				return await operation().ConfigureAwait(false);
			}
			catch (Exception ex) when (
				IsTransient(ex) && retry < MaxRetries && !cancellationToken.IsCancellationRequested)
			{
				logger.LogWarning(ex,
					"Redis is not reachable during startup ({Operation}). "
					+ "Retrying in {Interval}s ({Retry}/{MaxRetries}).",
					operationName, RetryInterval.TotalSeconds, retry + 1, MaxRetries);
				await Task.Delay(RetryInterval, cancellationToken).ConfigureAwait(false);
			}
		}
	}

	public static Task ExecuteAsync(
		Func<Task> operation,
		ILogger logger,
		string operationName,
		CancellationToken cancellationToken = default)
	{
		return ExecuteAsync<bool>(async () =>
		{
			await operation().ConfigureAwait(false);
			return true;
		}, logger, operationName, cancellationToken);
	}

	private static bool IsTransient(Exception ex)
	{
		return ex is RedisConnectionException or RedisTimeoutException or TimeoutException;
	}
}
