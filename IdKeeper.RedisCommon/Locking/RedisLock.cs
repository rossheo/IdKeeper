using IdKeeper.Database.Redis.Scripts;
using StackExchange.Redis;

namespace IdKeeper.Database.Redis.Locking;

// TickerQ 크론잡 등 여러 라운드트립에 걸친 작업의 다중 인스턴스 중복 실행을 막기 위한
// 단일 노드 락(SET NX PX + 토큰 기반 조건부 DEL). RedLock 쿼럼은 이 프로젝트의
// Redis 토폴로지(단일 인스턴스/Cluster 무관하게 락 키는 항상 한 노드에만 존재)에서
// 이득이 없어 사용하지 않는다.
public sealed class RedisLock : IAsyncDisposable
{
	private readonly IDatabase _db;
	private readonly LuaScriptLoader _scripts;
	private readonly RedisKey _key;
	private readonly RedisValue _token;
	private bool _released;

	public bool IsAcquired { get; }

	internal RedisLock(IDatabase db, LuaScriptLoader scripts, RedisKey key, RedisValue token, bool isAcquired)
	{
		_db = db;
		_scripts = scripts;
		_key = key;
		_token = token;
		IsAcquired = isAcquired;
	}

	public async ValueTask DisposeAsync()
	{
		if (_released || !IsAcquired)
		{
			return;
		}

		_released = true;
		await _db.ScriptEvaluateAsync(_scripts.Load("ReleaseLockIfOwned"), [_key], [_token]);
	}
}
