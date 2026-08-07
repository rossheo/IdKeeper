using IdKeeper.ApiService.Settings;
using IdKeeper.Database.Redis.Models;
using IdKeeper.Database.Redis.Repositories;

namespace IdKeeper.ApiService.ClockSkew;

/// <summary>
/// 판정 결과. HTTP 표현은 컨트롤러가 결정한다 — 이 클래스는 IActionResult를 만들지 않는다.
/// </summary>
public sealed record ClockSkewVerdict(bool Reject, TimeSpan Skew, string? RejectMessage);

/// <summary>
/// 클라이언트가 보낸 시각과 서버 시각을 비교해 시계 오차를 판정·기록한다.
///
/// 클라이언트는 자기 로컬 시계로 만료를 판단해 발급을 멈추지만, 서버가 노드 ID를 실제로
/// 회수·재할당하는 시점은 만료 + CleanupGracePeriod다. 클라이언트 시계가 뒤처져 있으면 서버가
/// 이미 넘긴 노드 ID를 계속 쓰게 되어 SnowflakeId가 중복된다. 클라이언트는 이 방향을 스스로
/// 검출할 수 없어(서버 시각이 필요하다) 서버가 판정한다.
///
/// 임계값은 CleanupGracePeriod 하나에서 파생한다. 별도 설정을 두면 유예 기간과 조용히 어긋나
/// 유예가 덮으려던 중복 창을 되살릴 수 있다.
/// </summary>
public sealed class ClockSkewPolicy(
	IdKeeperSetting setting,
	ClockSkewRepository clockSkewRepository,
	ILogger<ClockSkewPolicy> logger)
{
	private static readonly ClockSkewVerdict NotEvaluated = new(false, TimeSpan.Zero, null);

	/// <param name="allowReject">
	/// Alloc은 true, Renew는 false. Renew를 거부하면 지금 안전하게 갱신 중인 프로세스를 죽이게
	/// 되고, NTP 장애는 보통 클러스터 단위라 전면 장애로 번진다.
	/// </param>
	public async Task<ClockSkewVerdict> EvaluateAsync(
		string operation, string requester, DateTimeOffset? clientUtcNow, string? remoteIp,
		bool allowReject, CancellationToken cancellationToken = default)
	{
		// 값을 보내지 않는 클라이언트는 검사 대상이 아니다 — 로그도, 기록도, 거부도 없다.
		// 이 가드 덕분에 서버/클라이언트 배포 순서가 양방향으로 안전하다.
		if (clientUtcNow is null)
		{
			return NotEvaluated;
		}

		// 양수 = 클라이언트가 서버보다 뒤처짐 = 위험한 방향.
		TimeSpan skew = DateTime.UtcNow - clientUtcNow.Value.UtcDateTime;

		TimeSpan grace = setting.CleanupGracePeriod;
		bool reject = grace > TimeSpan.Zero && allowReject && skew > grace;

		// 등급을 함께 저장한다. 임계값은 ApiService 설정이라 Web(관리 화면·알림 Job)은 알 수
		// 없으므로, 판정 결과를 남겨야 양쪽이 임계값을 따로 관리하지 않는다.
		// Renew는 거부하지 않지만 유예를 넘은 오차는 Reject 등급으로 남긴다 — 그 클라이언트가
		// 재기동하면 Alloc에서 실제로 거부되므로, 알림 대상으로는 동일하게 다뤄야 한다.
		ClockSkewSeverity severity = ClockSkewSeverity.None;
		if (grace > TimeSpan.Zero)
		{
			TimeSpan warnBand = grace / 2;
			if (skew > grace)
			{
				severity = ClockSkewSeverity.Reject;
			}
			else if (skew > warnBand || skew < -warnBand)
			{
				severity = ClockSkewSeverity.Warn;
			}
		}

		await RecordAsync(requester, skew, operation, severity, remoteIp, cancellationToken);

		// CleanupGracePeriod=0은 문서화된 "유예 없음" 탈출구다. 이때 skew > grace로 비교하면
		// 1초 오차로도 거부되므로, 밴드 계산 전에 강제를 끈다.
		if (grace <= TimeSpan.Zero)
		{
			logger.LogInformation(
				"Clock skew observed but enforcement is disabled (CleanupGracePeriod=0)." +
				" Operation={Operation} Requester={Requester} SkewSeconds={SkewSeconds}",
				operation, requester, (Int64)skew.TotalSeconds);
			return new ClockSkewVerdict(false, skew, null);
		}

		if (reject)
		{
			string message =
				$"Client clock is behind the server by {(Int64)skew.TotalSeconds}s," +
				$" which exceeds the allowed {(Int64)grace.TotalSeconds}s (CleanupGracePeriod)." +
				" Allocating now risks duplicate Snowflake IDs." +
				" Synchronize the client host clock (NTP) and retry.";

			// 최초 Alloc이 거부되면 AllocatedId 행이 생기지 않아 관리 화면에 나타나지 않는다.
			// 이 로그가 가장 중요한 케이스의 유일한 채널이므로 조치에 필요한 값을 모두 남긴다.
			logger.LogWarning(
				"Rejected {Operation} due to client clock skew." +
				" Requester={Requester} SkewSeconds={SkewSeconds}" +
				" ThresholdSeconds={ThresholdSeconds} RemoteIp={RemoteIp}",
				operation, requester, (Int64)skew.TotalSeconds, (Int64)grace.TotalSeconds, remoteIp);

			return new ClockSkewVerdict(true, skew, message);
		}

		// 경고 밴드는 유예의 절반. 거부 임계값과 정상 사이에 완충을 둬 운영자가 손 쓸 구간을
		// 만든다. 앞선 방향(음수)은 클라이언트가 더 일찍 멈추므로 안전하지만, 시계 고장
		// 신호이므로 침묵하지 않는다. 거부 판정과 달리 여기서는 양방향을 본다.
		TimeSpan warn = grace / 2;
		if (skew > warn || skew < -warn)
		{
			logger.LogWarning(
				"Client clock skew exceeds the warning band." +
				" Operation={Operation} Requester={Requester} SkewSeconds={SkewSeconds}" +
				" WarnThresholdSeconds={WarnThresholdSeconds} RemoteIp={RemoteIp}",
				operation, requester, (Int64)skew.TotalSeconds, (Int64)warn.TotalSeconds, remoteIp);
		}

		return new ClockSkewVerdict(false, skew, null);
	}

	private async Task RecordAsync(
		string requester, TimeSpan skew, string operation, ClockSkewSeverity severity, string? remoteIp,
		CancellationToken cancellationToken)
	{
		// 오차가 클 때만이 아니라 항상 기록한다 — 값이 클 때만 쓰면 운영자가 이 기능이 살아
		// 있는지 확인할 방법이 없다.
		// 관측값이 설명하는 리스가 살아 있을 수 있는 동안 유지한다.
		TimeSpan ttl = setting.LeaseDuration + setting.CleanupGracePeriod;

		try
		{
			await clockSkewRepository.RecordAsync(
				requester, skew, operation, severity, remoteIp, ttl, cancellationToken);
		}
		catch (Exception ex)
		{
			// 진단 데이터이므로 Redis 일시 장애가 정상 Alloc/Renew를 실패시켜선 안 된다.
			logger.LogWarning(ex,
				"Failed to record clock skew observation. Requester={Requester}", requester);
		}
	}
}
