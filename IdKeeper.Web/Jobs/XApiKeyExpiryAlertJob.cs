using IdKeeper.Common.Constants;
using IdKeeper.Database.Redis.Models;
using IdKeeper.Database.Redis.Repositories;
using TickerQ.Utilities.Base;

namespace IdKeeper.Web.Jobs;

/// <summary>
/// 매시 정각에 7일 이내 만료 예정인 X-API 키를 확인해 Discord 웹훅을 설정한 모든
/// 사용자에게 알린다. 만료 시점이 지날 때까지 조건이 유지되는 한 매시간 반복
/// 전송된다(CapacityAlertJob과 동일하게 별도 중복 방지 상태를 두지 않는다).
/// </summary>
public class XApiKeyExpiryAlertJob(
	ILogger<XApiKeyExpiryAlertJob> logger,
	XApiKeyRepository xApiKeyRepository,
	CredentialSettingsRepository credentialSettingsRepository,
	IHttpClientFactory httpClientFactory)
{
	public static class FunctionNames
	{
		public const string XApiKeyExpiryAlert = "XApiKeyExpiryAlert";
	}

	private static readonly TimeSpan AlertWindow = TimeSpan.FromDays(7);
	private const Int32 MaxListedKeys = 20;

	[TickerFunction(functionName: FunctionNames.XApiKeyExpiryAlert,
		cronExpression: "0 0 * * * *")]
	public async Task XApiKeyExpiryAlert(
		TickerFunctionContext _, CancellationToken cancellationToken)
	{
		try
		{
			DateTime utcNow = DateTime.UtcNow;
			DateTime alertThreshold = utcNow + AlertWindow;

			List<XApiKey> allKeys = await xApiKeyRepository.GetAllAsync(cancellationToken);
			List<XApiKey> expiringKeys = [.. allKeys
				.Where(k => k.ExpiredAtUtc is not null
					&& k.ExpiredAtUtc > utcNow
					&& k.ExpiredAtUtc <= alertThreshold)
				.OrderBy(k => k.ExpiredAtUtc)];

			if (expiringKeys.Count == 0)
			{
				return;
			}

			List<string> webhookUrls =
				await credentialSettingsRepository.GetAllDiscordWebhookUrlsAsync(cancellationToken);
			if (webhookUrls.Count == 0)
			{
				logger.LogWarning(
					"{FunctionName}: {Count} key(s) expiring within 7 days but no Discord webhook is configured.",
					FunctionNames.XApiKeyExpiryAlert, expiringKeys.Count);
				return;
			}

			string message = BuildMessage(expiringKeys);

			HttpClient client = httpClientFactory.CreateClient(nameof(XApiKeyExpiryAlertJob));
			foreach (string webhookUrl in webhookUrls)
			{
				try
				{
					HttpResponseMessage response = await client.PostAsJsonAsync(
						webhookUrl, new { content = message }, cancellationToken);
					if (!response.IsSuccessStatusCode)
					{
						logger.LogWarning("{FunctionName}: Discord webhook responded with {StatusCode}.",
							FunctionNames.XApiKeyExpiryAlert, response.StatusCode);
					}
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "{FunctionName}: Failed to send a Discord webhook.",
						FunctionNames.XApiKeyExpiryAlert);
				}
			}

			logger.LogInformation(
				"{FunctionName} sent an expiry alert for {KeyCount} key(s) to {WebhookCount} webhook(s).",
				FunctionNames.XApiKeyExpiryAlert, expiringKeys.Count, webhookUrls.Count);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error during {FunctionName}: {Message}",
				FunctionNames.XApiKeyExpiryAlert, ex.Message);
		}
	}

	// Discord webhook의 content는 2000자 제한이 있어, 만료 임박 키가 많을 경우를 대비해
	// 상위 N개만 나열하고 나머지는 개수만 표시한다.
	private static string BuildMessage(List<XApiKey> expiringKeys)
	{
		IEnumerable<string> lines = expiringKeys.Take(MaxListedKeys).Select(k =>
			$"- {MaskConstant.MaskApiKey(k.ApiKey)} (Owner: {k.Owner}) — " +
			$"{DateTimeConstant.GetRemainTime(k.ExpiredAtUtc)} 후 만료 ({k.ExpiredAtUtc:yyyy-MM-dd HH:mm} UTC)");

		string header = $"⏰ X-API 키 {expiringKeys.Count}개가 7일 이내 만료됩니다.";
		string body = string.Join('\n', lines);

		Int32 omitted = expiringKeys.Count - MaxListedKeys;
		string footer = omitted > 0 ? $"\n...외 {omitted}개" : string.Empty;

		return $"{header}\n{body}{footer}";
	}
}
