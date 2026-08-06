namespace IdKeeper.Database.Redis.Models;

/// <summary>
/// 서버가 Alloc/Renew 요청 시점에 관측한 클라이언트 시계 오차. 진단·관측용 기록이며
/// 할당 자체의 정합성에는 관여하지 않는다.
/// </summary>
public class ClockSkewObservation
{
	/// <summary>
	/// serverNow - clientUtcNow. 부호 있음 — 양수는 클라이언트가 서버보다 뒤처졌다는 뜻이고,
	/// 이 방향이 회수된 노드 ID를 계속 쓰게 되는 중복 발급 위험이다. 음수(앞섬)는 클라이언트가
	/// 더 일찍 발급을 멈추므로 안전하다.
	/// </summary>
	public Int64 SkewSeconds { get; set; }

	public DateTime ObservedAtUtc { get; set; }

	/// <summary>관측이 이루어진 작업 — "Alloc" 또는 "Renew".</summary>
	public string Operation { get; set; } = string.Empty;

	/// <summary>
	/// 이 관측으로 요청이 거부됐는지. 판정 결과를 저장하는 이유: IdKeeper.Web은
	/// IdKeeper.ApiService.Settings를 참조하지 않아 임계값(CleanupGracePeriod)을 알 수 없고,
	/// 관리 화면이 밴드를 재계산하면 임계값을 두 곳에서 손으로 관리하게 된다.
	/// </summary>
	public bool Rejected { get; set; }

	/// <summary>어느 호스트의 시계가 틀렸는지 특정하기 위한 값.</summary>
	public string? RemoteIp { get; set; }
}
