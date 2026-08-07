namespace IdKeeper.Database.Redis.Models;

/// <summary>
/// 서버가 판정한 시계 오차 등급. 임계값(CleanupGracePeriod)은 IdKeeper.ApiService 설정이라
/// Web에서는 알 수 없으므로, 판정 결과를 저장해 화면·알림이 임계값을 재계산하지 않게 한다.
/// </summary>
public enum ClockSkewSeverity
{
	None = 0,
	/// <summary>유예의 절반을 초과 — 아직 발급은 되지만 예산을 잠식하고 있다.</summary>
	Warn = 1,
	/// <summary>유예를 초과해 Alloc이 거부됨 — 그 클라이언트는 기동하지 못한다.</summary>
	Reject = 2,
}

/// <summary>
/// 서버가 Alloc/Renew 요청 시점에 관측한 클라이언트 시계 오차. 진단·관측용 기록이며
/// 할당 자체의 정합성에는 관여하지 않는다.
/// </summary>
public class ClockSkewObservation
{
	public string Requester { get; set; } = string.Empty;

	/// <summary>
	/// serverNow - clientUtcNow. 부호 있음 — 양수는 클라이언트가 서버보다 뒤처졌다는 뜻이고,
	/// 이 방향이 회수된 노드 ID를 계속 쓰게 되는 중복 발급 위험이다. 음수(앞섬)는 클라이언트가
	/// 더 일찍 발급을 멈추므로 안전하다.
	/// </summary>
	public Int64 SkewSeconds { get; set; }

	public DateTime ObservedAtUtc { get; set; }

	/// <summary>관측이 이루어진 작업 — "Alloc" 또는 "Renew".</summary>
	public string Operation { get; set; } = string.Empty;

	public ClockSkewSeverity Severity { get; set; }

	/// <summary>어느 호스트의 시계가 틀렸는지 특정하기 위한 값.</summary>
	public string? RemoteIp { get; set; }
}
