using IdKeeper.ApiService.Settings;
using IdKeeper.Database.Redis;
using IdKeeper.Database.Redis.Models;
using IdKeeper.Database.Redis.Repositories;
using StackExchange.Redis;
using System.Net;
using System.Net.Sockets;

namespace IdKeeper.ApiService.Caching;

public sealed class CidrCacheRefreshService(
	CidrCache cidrCache,
	IConnectionMultiplexer multiplexer,
	XApiAllowedCidrRepository cidrRepository,
	XApiAllowedHostnameRepository hostnameRepository,
	IdKeeperSetting setting,
	ILogger<CidrCacheRefreshService> logger) : BackgroundService
{
	// 호스트명 DNS 재해석 실패 시(일시적 NXDOMAIN/타임아웃) 접근을 바로 막지 않고
	// 마지막으로 성공한 해석 결과를 유지하기 위한 상태. ExecuteAsync의 단일 루프에서만
	// 순차 접근하므로 동시성 보호가 필요 없다.
	private readonly Dictionary<string, IPNetwork[]> _lastKnownGoodByHostname =
		new(StringComparer.OrdinalIgnoreCase);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		await RefreshAsync(stoppingToken);

		ISubscriber subscriber = multiplexer.GetSubscriber();
		await subscriber.SubscribeAsync(RedisChannel.Literal(RedisKeyNames.PubSub.CidrChanged), async (_, _) =>
		{
			logger.LogInformation("CIDR change notification received. Refreshing cache.");
			await RefreshAsync(stoppingToken);
		});

		// DDNS 호스트명의 IP는 Pub/Sub 알림 없이도 바뀔 수 있으므로 주기적으로 재해석한다.
		using PeriodicTimer timer = new(setting.HostnameResolveInterval);
		while (await timer.WaitForNextTickAsync(stoppingToken))
		{
			logger.LogInformation("Periodic hostname re-resolution interval elapsed. Refreshing cache.");
			await RefreshAsync(stoppingToken);
		}
	}

	private async Task RefreshAsync(CancellationToken ct)
	{
		try
		{
			List<XApiAllowedCidr> cidrEntries = await cidrRepository.GetAllAsync(ct);
			List<XApiAllowedHostname> hostnameEntries = await hostnameRepository.GetAllAsync(ct);

			List<IPNetwork> networks = [.. cidrEntries
				.Where(c => IPNetwork.TryParse(c.Cidr, out _))
				.Select(c => IPNetwork.Parse(c.Cidr))];

			foreach (XApiAllowedHostname hostnameEntry in hostnameEntries)
			{
				networks.AddRange(await ResolveHostnameAsync(hostnameEntry.Hostname, ct));
			}

			cidrCache.Update([.. networks]);
			logger.LogInformation(
				"CIDR cache refreshed. Cidr: {CidrCount}, Hostname: {HostnameCount}, TotalNetworks: {Total}",
				cidrEntries.Count, hostnameEntries.Count, networks.Count);
		}
		catch (Exception ex) when (!ct.IsCancellationRequested)
		{
			logger.LogError(ex, "Failed to refresh CIDR cache.");
		}
	}

	private async Task<IPNetwork[]> ResolveHostnameAsync(string hostname, CancellationToken ct)
	{
		try
		{
			IPAddress[] addresses = await Dns.GetHostAddressesAsync(hostname, ct);
			if (addresses.Length == 0)
			{
				logger.LogWarning(
					"DDNS hostname {Hostname} resolved to zero addresses. Using last known IP if any.",
					hostname);
				return _lastKnownGoodByHostname.GetValueOrDefault(hostname, []);
			}

			IPNetwork[] resolved = [.. addresses.Select(ip => new IPNetwork(
				ip, ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128))];

			_lastKnownGoodByHostname[hostname] = resolved;
			return resolved;
		}
		catch (Exception ex) when (!ct.IsCancellationRequested)
		{
			// 일시적 DNS 장애로 정상 트래픽이 갑자기 막히지 않도록, 실패 시 마지막으로
			// 성공한 해석 결과를 그대로 유지한다(빈 CIDR 목록=전체 허용과 같은 가용성 우선 철학).
			logger.LogWarning(ex,
				"Failed to resolve DDNS hostname {Hostname}. Using last known IP if any.",
				hostname);
			return _lastKnownGoodByHostname.GetValueOrDefault(hostname, []);
		}
	}
}
