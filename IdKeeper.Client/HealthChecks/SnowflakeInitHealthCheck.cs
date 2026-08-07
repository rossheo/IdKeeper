using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IdKeeper.Client.HealthChecks;

internal sealed class SnowflakeInitHealthCheck : IHealthCheck
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
		bool isReady = await _snowflakeHostedService.IsReadyAsync(cancellationToken).ConfigureAwait(false);

		return isReady
			? HealthCheckResult.Healthy()
			: HealthCheckResult.Unhealthy("Snowflake node ID not yet allocated.");
	}
}
