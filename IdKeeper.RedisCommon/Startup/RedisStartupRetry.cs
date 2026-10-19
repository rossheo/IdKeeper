using System.Diagnostics;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace IdKeeper.Database.Redis.Startup;

// 기동 시점의 Redis 접근은 Redis 파드가 재생성되는 중이면 한동안 실패할 수 있다(순차 재생성은 수 분이 걸린다).
// 한 번 실패했다고 프로세스를 종료시키면 컨테이너가 재시작을 반복하므로, 연결/타임아웃 오류에 한해
// 지수 백오프(+지터)로 총 시한 안에서 계속 재시도한다. 시한을 넘기면 마지막 예외를 그대로 던진다.
// 기동이 이 시간 동안 이어지므로 쿠버네티스 startupProbe는 TotalTimeout보다 길게 잡아야 한다.
public static class RedisStartupRetry
{
	public static readonly TimeSpan TotalTimeout = TimeSpan.FromMinutes(10);

	public static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(2);

	public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(20);

	// 지연 시간에 ±20%의 지터를 섞어 여러 인스턴스가 동시에 재시도하지 않게 한다.
	private const double JitterRatio = 0.2;

	public static async Task<T> ExecuteAsync<T>(
		Func<Task<T>> operation,
		ILogger logger,
		string operationName,
		CancellationToken cancellationToken = default)
	{
		long startTimestamp = Stopwatch.GetTimestamp();

		for (Int32 attempt = 1; ; attempt++)
		{
			try
			{
				return await operation().ConfigureAwait(false);
			}
			catch (Exception ex) when (
				IsTransient(ex)
				&& !cancellationToken.IsCancellationRequested
				&& Stopwatch.GetElapsedTime(startTimestamp) < TotalTimeout)
			{
				TimeSpan elapsed = Stopwatch.GetElapsedTime(startTimestamp);
				TimeSpan delay = GetDelay(attempt);
				TimeSpan remaining = TotalTimeout - elapsed;
				if (delay > remaining)
				{
					delay = remaining;
				}

				logger.LogWarning(ex,
					"Redis is not reachable during startup ({Operation}). "
					+ "Retrying in {Delay:F1}s (attempt {Attempt}, elapsed {Elapsed:F0}s of {Total:F0}s).",
					operationName, delay.TotalSeconds, attempt,
					elapsed.TotalSeconds, TotalTimeout.TotalSeconds);
				await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
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

	// attempt번째 실패 뒤의 대기 시간: InitialDelay * 2^(attempt-1), MaxDelay 상한, 지터 적용.
	internal static TimeSpan GetDelay(Int32 attempt)
	{
		double exponent = Math.Min(attempt - 1, 30);
		double seconds = Math.Min(InitialDelay.TotalSeconds * Math.Pow(2, exponent), MaxDelay.TotalSeconds);
		double jitter = 1 + ((Random.Shared.NextDouble() * 2) - 1) * JitterRatio;
		return TimeSpan.FromSeconds(seconds * jitter);
	}

	private static bool IsTransient(Exception ex)
	{
		return ex is RedisConnectionException or RedisTimeoutException or TimeoutException;
	}
}
