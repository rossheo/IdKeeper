namespace IdKeeper.ApiService.Requests;

public class IdKeeperRequestV1Renew
{
	public string Requester { get; set; } = string.Empty;

	// IdKeeperRequestV1Alloc.ClientUtcNow와 동일한 목적. 단 Renew는 이 값으로 거부하지 않고
	// 경고만 남긴다 — 갱신이 성공하는 동안에는 만료 시각이 계속 밀려 회수 시점에 도달하지
	// 않으므로 시계 오차만으로는 ID가 중복되지 않는다.
	public DateTimeOffset? ClientUtcNow { get; set; }
}
