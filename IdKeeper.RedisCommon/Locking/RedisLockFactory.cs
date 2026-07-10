using IdKeeper.Database.Redis.Scripts;
using StackExchange.Redis;

namespace IdKeeper.Database.Redis.Locking;

public sealed class RedisLockFactory(IConnectionMultiplexer multiplexer, LuaScriptLoader scripts)
{
	public async Task<RedisLock> TryAcquireAsync(string key, TimeSpan expiry)
	{
		IDatabase db = multiplexer.GetDatabase();
		RedisValue token = Guid.NewGuid().ToString("N");
		bool isAcquired = await db.StringSetAsync(key, token, expiry, When.NotExists);
		return new RedisLock(db, scripts, key, token, isAcquired);
	}
}
