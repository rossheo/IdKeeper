namespace IdKeeper.ApiService.Requests;

public class IdKeeperRequestV1Alloc
{
	public Int32 Count { get; set; }
	public string Requester { get; set; } = string.Empty;

	// 서버가 "클라이언트 시계가 뒤처진" 방향을 검출하기 위한 값. 이 값을 보내지 않는
	// 클라이언트와의 호환을 위해 nullable이며, null이면 검사 자체를 건너뛴다.
	// DateTime이 아니라 DateTimeOffset을 쓴다 — 오프셋이 붙은 문자열이 와도 순간(instant)이
	// 모호해지지 않는다. FluentValidation 규칙은 두지 않는다(이상값은 400이 아니라 건너뛰기).
	public DateTimeOffset? ClientUtcNow { get; set; }
}
