using IdKeeper.Database.Redis.Models;
using IdKeeper.Database.Redis.Repositories;
using TickerQ.Utilities.Base;

namespace IdKeeper.Web.Jobs;

/// <summary>
/// 매시 정각에 클라이언트 시계 오차 관측값을 확인해, 유예(CleanupGracePeriod)를 넘었거나
/// 그 절반을 넘은 클라이언트가 있으면 Discord 웹훅을 설정한 모든 사용자에게 알린다.
///
/// 등급 판정은 서버(IdKeeper.ApiService)가 이미 저장해 둔 값을 그대로 쓴다 — 임계값이
/// ApiService 설정이라 Web에서는 알 수 없고, 여기서 재계산하면 임계값을 두 곳에서 손으로
/// 맞춰야 한다.
///
/// AllocatedId 쪽에서 requester를 역추적하지 않고 ClockSkew 인덱스를 쓰는 이유: 거부된
/// 클라이언트는 노드 Id를 받지 못해 할당 목록에 없는데, 정작 그쪽이 가장 급한 대상이다.
///
/// CapacityAlertJob과 마찬가지로 상태 기반 알림이라 조건이 해소될 때까지 매시간 반복된다
/// (마일스톤 1회성인 SnowflakeWraparoundAlertJob과 다르다).
/// </summary>
public class ClockSkewAlertJob(
	ILogger<ClockSkewAlertJob> logger,
	ClockSkewRepository clockSkewRepository,
	CredentialSettingsRepository credentialSettingsRepository,
	IHttpClientFactory httpClientFactory)
{
	public static class FunctionNames
	{
		public const string ClockSkewAlert = "ClockSkewAlert";
	}

	// 메시지가 지나치게 길어지지 않도록 상위 몇 건만 나열한다.
	private const Int32 MaxListedRequesters = 10;

	[TickerFunction(functionName: FunctionNames.ClockSkewAlert,
		cronExpression: "0 0 * * * *")]
	public async Task ClockSkewAlert(
		TickerFunctionContext _, CancellationToken cancellationToken)
	{
		try
		{
			List<ClockSkewObservation> all = await clockSkewRepository.GetAllAsync(cancellationToken);

			// 오차가 큰 순서로 — 거부 등급을 먼저, 그다음 오차 절댓값이 큰 순서.
			List<ClockSkewObservation> problems =
			[
				.. all.Where(o => o.Severity != ClockSkewSeverity.None)
					.OrderByDescending(o => o.Severity)
					.ThenByDescending(o => Math.Abs(o.SkewSeconds))
			];

			if (problems.Count == 0)
			{
				return;
			}

			Int32 rejectCount = problems.Count(o => o.Severity == ClockSkewSeverity.Reject);
			Int32 warnCount = problems.Count - rejectCount;

			List<string> webhookUrls =
				await credentialSettingsRepository.GetAllDiscordWebhookUrlsAsync(cancellationToken);
			if (webhookUrls.Count == 0)
			{
				logger.LogWarning(
					"{FunctionName}: {RejectCount} rejected / {WarnCount} warning clients" +
					" but no Discord webhook is configured.",
					FunctionNames.ClockSkewAlert, rejectCount, warnCount);
				return;
			}

			string message = BuildMessage(problems, rejectCount, warnCount);

			HttpClient client = httpClientFactory.CreateClient(nameof(ClockSkewAlertJob));
			foreach (string webhookUrl in webhookUrls)
			{
				try
				{
					HttpResponseMessage response = await client.PostAsJsonAsync(
						webhookUrl, new { content = message }, cancellationToken);
					if (!response.IsSuccessStatusCode)
					{
						logger.LogWarning("{FunctionName}: Discord webhook responded with {StatusCode}.",
							FunctionNames.ClockSkewAlert, response.StatusCode);
					}
				}
				catch (Exception ex)
				{
					logger.LogError(ex, "{FunctionName}: Failed to send a Discord webhook.",
						FunctionNames.ClockSkewAlert);
				}
			}

			logger.LogInformation(
				"{FunctionName} sent a clock-skew alert to {Count} webhook(s)." +
				" Rejected={RejectCount} Warning={WarnCount}",
				FunctionNames.ClockSkewAlert, webhookUrls.Count, rejectCount, warnCount);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error during {FunctionName}: {Message}",
				FunctionNames.ClockSkewAlert, ex.Message);
		}
	}

	private static string BuildMessage(
		List<ClockSkewObservation> problems, Int32 rejectCount, Int32 warnCount)
	{
		System.Text.StringBuilder builder = new();
		builder.Append("⚠️ IdKeeper 클라이언트 시계 오차 감지 — ");
		builder.Append($"거부 {rejectCount}건, 경고 {warnCount}건");
		builder.AppendLine();
		builder.AppendLine("거부된 클라이언트는 노드 Id를 받지 못해 기동하지 못합니다. 해당 호스트의 NTP 동기화를 확인하세요.");

		foreach (ClockSkewObservation o in problems.Take(MaxListedRequesters))
		{
			string mark = o.Severity == ClockSkewSeverity.Reject ? "❌" : "⚠️";
			string direction = o.SkewSeconds > 0 ? "뒤처짐" : (o.SkewSeconds < 0 ? "앞섬" : "동기");
			builder.AppendLine(
				$"{mark} `{o.Requester}` {o.SkewSeconds:+#;-#;0}s ({direction})" +
				$" ip={o.RemoteIp ?? "-"} 관측={o.ObservedAtUtc:yyyy-MM-dd HH:mm}Z ({o.Operation})");
		}

		if (problems.Count > MaxListedRequesters)
		{
			builder.AppendLine($"… 외 {problems.Count - MaxListedRequesters}건");
		}

		return builder.ToString();
	}
}
