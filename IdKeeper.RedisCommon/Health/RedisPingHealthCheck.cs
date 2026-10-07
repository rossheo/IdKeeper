using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace IdKeeper.Database.Redis.Health;

// Redis 클러스터에서 HealthChecks.Redis 기본 체크는 연결이 정상이어도 항상 Unhealthy가 되므로
// 실제 PING 성공 여부로 판정한다. 파드 재생성으로 Redis IP가 바뀌면 멀티플렉서가 옛 엔드포인트를
// 붙잡고 복구되지 않는데, 이 체크가 실패하면서 프로브가 파드를 재시작하게 만든다.
// 일부 마스터만 옛 IP에 갇히는 경우도 있어(나머지 마스터로는 PING이 성공함) 알려진 모든 마스터에
// 각각 PING을 보내 하나라도 실패하면 Unhealthy로 판정한다.
public sealed class RedisPingHealthCheck(IConnectionMultiplexer multiplexer) : IHealthCheck
{
	private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(3);

	public async Task<HealthCheckResult> CheckHealthAsync(
		HealthCheckContext context, CancellationToken cancellationToken = default)
	{
		try
		{
			IServer[] masters = multiplexer.GetServers().Where(static server => !server.IsReplica).ToArray();
			if (masters.Length == 0)
			{
				await multiplexer.GetDatabase().PingAsync().WaitAsync(PingTimeout, cancellationToken)
					.ConfigureAwait(false);
				return HealthCheckResult.Healthy();
			}

			string[] failed = (await Task.WhenAll(masters.Select(server => PingAsync(server, cancellationToken)))
				.ConfigureAwait(false)).OfType<string>().ToArray();
			return failed.Length == 0
				? HealthCheckResult.Healthy()
				: HealthCheckResult.Unhealthy($"Redis PING failed: {string.Join(", ", failed)}");
		}
		catch (Exception ex)
		{
			return HealthCheckResult.Unhealthy("Redis PING failed.", ex);
		}
	}

	// 실패한 엔드포인트 이름을 반환하고, 성공하면 null을 반환한다.
	private static async Task<string?> PingAsync(IServer server, CancellationToken cancellationToken)
	{
		try
		{
			await server.PingAsync().WaitAsync(PingTimeout, cancellationToken).ConfigureAwait(false);
			return null;
		}
		catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			return server.EndPoint.ToString();
		}
	}
}
