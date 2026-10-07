using System.Net;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace IdKeeper.Database.Redis.Health;

// Redis 클러스터에서 HealthChecks.Redis 기본 체크는 연결이 정상이어도 항상 Unhealthy가 되므로
// 실제 PING 성공 여부로 판정한다. 파드 재생성으로 Redis IP가 바뀌면 멀티플렉서가 옛 엔드포인트만
// 붙잡고 복구되지 않는데, 이 체크가 실패하면서 프로브가 파드를 재시작하게 만든다.
// 일부 마스터만 옛 IP에 갇히는 경우도 있어(나머지 마스터로는 PING이 성공함) 알려진 모든 마스터에
// 각각 PING을 보낸다. 다만 Redis 파드가 잠깐 내려간 일시 장애에서는 앱을 재시작해도 소용없으므로,
// PING에 실패한 IP 엔드포인트가 현재 클러스터 토폴로지(CLUSTER NODES)에 없을 때(= 옛 IP에 갇힘)만
// Unhealthy로 판정하고, 토폴로지에 남아 있는 노드의 실패는 일시 장애로 보고 Healthy로 둔다.
// 응답하는 마스터가 하나도 없으면 토폴로지를 확인할 수 없으므로 Unhealthy다.
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

			bool[] pinged = await Task.WhenAll(masters.Select(server => PingAsync(server, cancellationToken)))
				.ConfigureAwait(false);
			EndPoint[] failed = masters.Where((_, i) => !pinged[i]).Select(static server => server.EndPoint).ToArray();
			if (failed.Length == 0)
			{
				return HealthCheckResult.Healthy();
			}

			IServer? healthy = masters.Where((_, i) => pinged[i]).FirstOrDefault();
			if (healthy is null)
			{
				return HealthCheckResult.Unhealthy("Redis PING failed on every master.");
			}

			// 응답한 마스터에게 현재 토폴로지를 물어 실패한 IP 엔드포인트가 아직 클러스터에 있는지 확인한다.
			ClusterConfiguration? topology = await healthy.ClusterNodesAsync().WaitAsync(PingTimeout, cancellationToken)
				.ConfigureAwait(false);
			HashSet<EndPoint> current = topology?.Nodes.Where(static node => !node.IsFail).Select(static node => node.EndPoint).OfType<EndPoint>()
				.ToHashSet() ?? [];
			string[] stale = failed.OfType<IPEndPoint>().Where(endpoint => !current.Contains(endpoint))
				.Select(static endpoint => endpoint.ToString()).ToArray();
			return stale.Length == 0
				? HealthCheckResult.Healthy("Some Redis masters are temporarily unreachable.")
				: HealthCheckResult.Unhealthy($"Redis connection is stuck on stale endpoints: {string.Join(", ", stale)}");
		}
		catch (Exception ex)
		{
			return HealthCheckResult.Unhealthy("Redis PING failed.", ex);
		}
	}

	private static async Task<bool> PingAsync(IServer server, CancellationToken cancellationToken)
	{
		try
		{
			await server.PingAsync().WaitAsync(PingTimeout, cancellationToken).ConfigureAwait(false);
			return true;
		}
		catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			return false;
		}
	}
}
