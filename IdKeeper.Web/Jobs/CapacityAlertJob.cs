using IdKeeper.Common.Constants;
using IdKeeper.Database.Redis.Repositories;
using TickerQ.Utilities.Base;

namespace IdKeeper.Web.Jobs;

/// <summary>매시 정각에 Node ID 잔여율을 확인해 20% 미만이면 Discord 웹훅을 설정한 모든 사용자에게 알린다.</summary>
public class CapacityAlertJob(
	ILogger<CapacityAlertJob> logger,
	AllocatedIdRepository allocatedIdRepository,
	CredentialSettingsRepository credentialSettingsRepository,
	IHttpClientFactory httpClientFactory)
{
	public static class FunctionNames
	{
		public const string CapacityAlert = "CapacityAlert";
	}

	private const double RemainingPercentThreshold = 20.0;

	[TickerFunction(functionName: FunctionNames.CapacityAlert,
		cronExpression: "0 0 * * * *")]
	public async Task CapacityAlert(
		TickerFunctionContext _, CancellationToken cancellationToken)
	{
		try
		{
			Int32 total = 1 << SnowflakeConstant.BitCountOfNodeId;
			Int64 used = await allocatedIdRepository.CountOfAllocatedAsync(cancellationToken);
			double remainingPercent = total == 0 ? 0 : (1.0 - (double)used / total) * 100.0;

			if (remainingPercent >= RemainingPercentThreshold)
			{
				return;
			}

			List<string> webhookUrls =
				await credentialSettingsRepository.GetAllDiscordWebhookUrlsAsync(cancellationToken);
			if (webhookUrls.Count == 0)
			{
				logger.LogWarning(
					"{FunctionName}: RemainingPercent={RemainingPercent:F1} but no Discord webhook is configured.",
					FunctionNames.CapacityAlert, remainingPercent);
				return;
			}

			string message = $"⚠️ IdKeeper Node ID 여유가 {remainingPercent:F1}%로 낮습니다. " +
				$"(사용 {used:N0} / 전체 {total:N0})";

			HttpClient client = httpClientFactory.CreateClient(nameof(CapacityAlertJob));
			foreach (string webhookUrl in webhookUrls)
			{
				try
				{
					HttpResponseMessage response = await client.PostAsJsonAsync(
						webhookUrl, new { content = message }, cancellationToken);
					if (!response.IsSuccessStatusCode)
					{
						logger.LogWarning("{FunctionName}: Discord webhook responded with {StatusCode}.",
							FunctionNames.CapacityAlert, response.StatusCode);
					}
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "{FunctionName}: Failed to send a Discord webhook.",
						FunctionNames.CapacityAlert);
				}
			}

			logger.LogInformation(
				"{FunctionName} sent a low-capacity alert to {Count} webhook(s). RemainingPercent={RemainingPercent:F1}",
				FunctionNames.CapacityAlert, webhookUrls.Count, remainingPercent);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error during {FunctionName}: {Message}",
				FunctionNames.CapacityAlert, ex.Message);
		}
	}
}
