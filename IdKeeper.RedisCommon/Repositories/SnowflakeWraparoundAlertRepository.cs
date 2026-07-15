using StackExchange.Redis;

namespace IdKeeper.Database.Redis.Repositories;

public sealed class SnowflakeWraparoundAlertRepository(IConnectionMultiplexer multiplexer)
{
	private IDatabase Db => multiplexer.GetDatabase();

	/// <summary>
	/// 주어진 (wrap-around 시점, 마일스톤) 조합으로 이미 알림을 보냈는지 원자적으로 확인하고,
	/// 처음이면 "보냄"으로 표시한다. 반환값이 true면 지금 처음 보내는 것이므로 알림을 전송해야 한다.
	/// </summary>
	public async Task<bool> TryMarkSentAsync(
		DateTime wraparoundDateUtc, string milestone, CancellationToken cancellationToken = default)
	{
		RedisKey key = RedisKeyNames.SnowflakeWraparoundAlert.Sent(wraparoundDateUtc.Ticks, milestone);
		return await Db.StringSetAsync(key, "1", when: When.NotExists);
	}
}
