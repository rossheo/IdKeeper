using System.ComponentModel.DataAnnotations;

namespace IdKeeper.ApiService.Settings;

public sealed class IdKeeperSetting
{
	[Required, Range(1, 100)]
	public Int32 MaxAllocCount { get; set; }

	// Minimum 50 minutes, maximum 14 days
	[Required, Range(typeof(TimeSpan), "00:50:00", "14.00:00:00")]
	public TimeSpan LeaseDuration { get; set; }

	[Required, Range(typeof(TimeSpan), "00:01:00", "00:20:00")]
	public TimeSpan FirstTimeExpiration { get; set; } = TimeSpan.FromMinutes(10);

	// 클라이언트는 자기 로컬 시계로 만료를 판단해 발급을 멈추지만, 노드 ID가 실제로 다른
	// 프로세스에게 넘어가는 시점은 CleanupExpiredJob이 비트맵 비트를 지울 때다. 클라이언트
	// 시계가 서버보다 뒤처져 있으면 서버가 이미 회수·재할당한 노드 ID를 클라이언트가 계속
	// 쓰는 구간이 생겨 Snowflake ID가 중복된다. 회수 기준 시각을 이만큼 과거로 당겨 그 오차를
	// 흡수한다 — 안전 조건이 "시계 오차 < 이 값"이라는 유한값이 된다. 시계 오차뿐 아니라
	// GC 스톨·컨테이너 프리즈·ApiService 레플리카 간 시계 차이도 함께 덮는다.
	//
	// ExpiryIndex의 점수를 미루지 않고 잡의 cutoff만 당기는 이유: ToggleIgnoreExpireAtomic이
	// IgnoreExpire를 끌 때 저장된 ExpiredAtUtc 원본으로 다시 ZADD하므로, 점수를 밀어두면 그
	// 경로에서 유예가 사라진다.
	//
	// cron이 10분 주기라 실효 회수 지연은 이 값 + [0, 10분]이다 (이 값은 하한이지 정확값이 아님).
	// [Required]를 붙이지 않는다 — non-nullable TimeSpan에서는 no-op이면서 0이 거부된다는
	// 오해를 준다. 0은 유예 없음(변경 전 동작)을 뜻하는 의도된 값이다.
	[Range(typeof(TimeSpan), "00:00:00", "01:00:00")]
	public TimeSpan CleanupGracePeriod { get; set; } = TimeSpan.FromMinutes(10);

	// DDNS 호스트명은 테이블 변경 없이도 IP가 바뀔 수 있어 주기적으로 재해석해야 한다.
	[Required, Range(typeof(TimeSpan), "00:00:10", "00:10:00")]
	public TimeSpan HostnameResolveInterval { get; set; } = TimeSpan.FromSeconds(60);
}
