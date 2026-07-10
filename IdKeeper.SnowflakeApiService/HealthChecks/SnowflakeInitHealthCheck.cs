using IdKeeper.SnowflakeApiService.HostedServices;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IdKeeper.SnowflakeApiService.HealthChecks;

public sealed class SnowflakeInitHealthCheck : IHealthCheck
{
	private readonly SnowflakeHostedService _snowflakeHostedService;

	public SnowflakeInitHealthCheck(SnowflakeHostedService snowflakeHostedService)
	{
		_snowflakeHostedService = snowflakeHostedService;
	}

	public async Task<HealthCheckResult> CheckHealthAsync(
		HealthCheckContext context,
		CancellationToken cancellationToken = default)
	{
		bool isReady = await _snowflakeHostedService.IsReadyAsync(cancellationToken);

		return isReady
			? HealthCheckResult.Healthy()
			: HealthCheckResult.Unhealthy("Snowflake node ID not yet allocated.");
	}
}
