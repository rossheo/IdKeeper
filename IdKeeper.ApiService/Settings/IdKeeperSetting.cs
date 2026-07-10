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

	// DDNS 호스트명은 테이블 변경 없이도 IP가 바뀔 수 있어 주기적으로 재해석해야 한다.
	[Required, Range(typeof(TimeSpan), "00:00:10", "00:10:00")]
	public TimeSpan HostnameResolveInterval { get; set; } = TimeSpan.FromSeconds(60);
}
