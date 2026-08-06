using IdKeeper.Database.Redis.Scripts;
using StackExchange.Redis;

namespace IdKeeper.Database.Redis.Locking;

// TickerQ Job에서 다중 인스턴스 중복 실행을 막을 때 이 락을 쓴다. 단, 아래 두 경우로 용도가 갈린다.
// - 멱등 Job(현재 CleanupExpiredJob, CleanupAuditLogJob): TryAcquireAsync + 완료 시 자동 해제(await using)
//   패턴을 그대로 쓴다. TTL은 최악 실행 시간보다 충분히 길게 잡는다 — 완료 시 즉시 해제되므로
//   크론 주기보다 길어도 무방하며, TTL은 프로세스가 죽었을 때의 상한선 역할만 한다.
// - 비멱등 Job: 이 패턴은 실행이 끝나면 즉시 락을 해제하므로 "같은 틱에서 한 번만" 실행됨을 보장하지
//   않는다(RedisLock.DisposeAsync 참고). 틱 시각을 락 키에 포함하고 TTL을 크론 주기 이상으로 잡아
//   완료 후에도 해제하지 않고 자연 만료시켜야 하는데, 현재 RedisLock은 Dispose 시 항상 해제하므로
//   이 용도로는 쓸 수 없다 — 필요해지면 "해제 없는 획득" 경로를 별도로 추가해야 한다.
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
