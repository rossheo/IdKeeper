using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace IdKeeper.Database.Redis.Health;

// Redis 클러스터에서 HealthChecks.Redis 기본 체크는 연결이 정상이어도 항상 Unhealthy가 되므로
// 실제 PING 성공 여부로 판정한다. 파드 재생성으로 Redis IP가 바뀌면 멀티플렉서가 옛 엔드포인트만
// 붙잡고 복구되지 않는데, 이 체크가 실패하면서 프로브가 파드를 재시작하게 만든다.
public sealed class RedisPingHealthCheck(IConnectionMultiplexer multiplexer) : IHealthCheck
{
	private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(3);

	public async Task<HealthCheckResult> CheckHealthAsync(
		HealthCheckContext context, CancellationToken cancellationToken = default)
	{
		try
		{
			await multiplexer.GetDatabase().PingAsync().WaitAsync(PingTimeout, cancellationToken)
				.ConfigureAwait(false);
			return HealthCheckResult.Healthy();
		}
		catch (Exception ex)
		{
			return HealthCheckResult.Unhealthy("Redis PING failed.", ex);
		}
	}
}
