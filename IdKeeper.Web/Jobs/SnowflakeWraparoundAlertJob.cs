using IdKeeper.Common.Constants;
using IdKeeper.Database.Redis.Repositories;
using TickerQ.Utilities.Base;

namespace IdKeeper.Web.Jobs;

/// <summary>
/// 매시 정각에 Snowflake 타임스탬프 wrap-around 시점까지 남은 기간을 확인해, 12개월/6개월/3개월/
/// 1개월/7일/1일 전 시점을 지날 때마다 Discord 웹훅을 설정한 모든 사용자에게 1회씩 알린다.
/// (CapacityAlertJob/XApiKeyExpiryAlertJob과 달리 마일스톤별 1회 전송이 목적이라
/// SnowflakeWraparoundAlertRepository로 전송 여부를 기록한다.)
/// </summary>
public class SnowflakeWraparoundAlertJob(
	ILogger<SnowflakeWraparoundAlertJob> logger,
	SnowflakeLayoutHolder snowflakeLayoutHolder,
	SnowflakeWraparoundAlertRepository wraparoundAlertRepository,
	CredentialSettingsRepository credentialSettingsRepository,
	IHttpClientFactory httpClientFactory)
{
	public static class FunctionNames
	{
		public const string SnowflakeWraparoundAlert = "SnowflakeWraparoundAlert";
	}

	// 남은 기간이 긴 순서로 정의 — 재시작/장애 등으로 여러 마일스톤을 한 번에 지나쳤다면
	// 이 순서대로 모두 밀린 알림을 보낸다.
	private static readonly (string Label, Func<DateTime, DateTime> ThresholdFromWraparound)[] Milestones =
	[
		("12개월", w => w.AddMonths(-12)),
		("6개월", w => w.AddMonths(-6)),
		("3개월", w => w.AddMonths(-3)),
		("1개월", w => w.AddMonths(-1)),
		("7일", w => w.AddDays(-7)),
		("1일", w => w.AddDays(-1)),
	];

	[TickerFunction(functionName: FunctionNames.SnowflakeWraparoundAlert,
		cronExpression: "0 0 * * * *")]
	public async Task SnowflakeWraparoundAlert(
		TickerFunctionContext _, CancellationToken cancellationToken)
	{
		try
		{
			DateTime? wraparoundDateUtc = snowflakeLayoutHolder.Current.WraparoundDateUtc;
			if (wraparoundDateUtc is null)
			{
				// 사실상 무제한(DateTime 표현 범위 초과) 레이아웃 — 알림 대상 아님.
				return;
			}

			DateTime wraparound = wraparoundDateUtc.Value;
			DateTime utcNow = DateTime.UtcNow;
			if (utcNow >= wraparound)
			{
				// 이미 wrap-around 지남 — 이 잡의 책임 범위 밖(타임스탬프가 이미 겹치기 시작한 상태).
				return;
			}

			foreach ((string label, Func<DateTime, DateTime> thresholdFn) in Milestones)
			{
				if (utcNow < thresholdFn(wraparound))
				{
					continue;
				}

				bool firstTime = await wraparoundAlertRepository.TryMarkSentAsync(
					wraparound, label, cancellationToken);
				if (!firstTime)
				{
					continue;
				}

				await SendAlertAsync(label, wraparound, cancellationToken);
			}
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error during {FunctionName}: {Message}",
				FunctionNames.SnowflakeWraparoundAlert, ex.Message);
		}
	}

	private async Task SendAlertAsync(string label, DateTime wraparoundDateUtc, CancellationToken cancellationToken)
	{
		List<string> webhookUrls =
			await credentialSettingsRepository.GetAllDiscordWebhookUrlsAsync(cancellationToken);
		if (webhookUrls.Count == 0)
		{
			logger.LogWarning(
				"{FunctionName}: {Milestone} milestone reached but no Discord webhook is configured.",
				FunctionNames.SnowflakeWraparoundAlert, label);
			return;
		}

		string message = $"⏰ IdKeeper Snowflake 타임스탬프 wrap-around까지 {label} 남았습니다. " +
			$"({DateTimeConstant.GetRemainTime(wraparoundDateUtc)} 후, {wraparoundDateUtc:yyyy-MM-dd HH:mm} UTC) " +
			"그 이후에는 타임스탬프가 되돌아가 ID가 과거 ID와 겹치거나 정렬이 깨질 수 있습니다. " +
			"/snowflakelayout에서 레이아웃을 점검하세요.";

		HttpClient client = httpClientFactory.CreateClient(nameof(SnowflakeWraparoundAlertJob));
		foreach (string webhookUrl in webhookUrls)
		{
			try
			{
				HttpResponseMessage response = await client.PostAsJsonAsync(
					webhookUrl, new { content = message }, cancellationToken);
				if (!response.IsSuccessStatusCode)
				{
					logger.LogWarning("{FunctionName}: Discord webhook responded with {StatusCode}.",
						FunctionNames.SnowflakeWraparoundAlert, response.StatusCode);
				}
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "{FunctionName}: Failed to send a Discord webhook.",
					FunctionNames.SnowflakeWraparoundAlert);
			}
		}

		logger.LogInformation(
			"{FunctionName} sent a {Milestone} wrap-around alert to {Count} webhook(s).",
			FunctionNames.SnowflakeWraparoundAlert, label, webhookUrls.Count);
	}
}
