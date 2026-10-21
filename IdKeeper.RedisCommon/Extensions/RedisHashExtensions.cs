using IdKeeper.Database.Redis.Scripts;
using StackExchange.Redis;

namespace IdKeeper.Database.Redis.Extensions;

internal static class RedisHashExtensions
{
	/// <summary>
	/// Hash가 존재할 때만 필드를 원자적으로 갱신한다. (HashSetIfExistsAtomic.lua)
	/// </summary>
	/// <returns>Hash가 없어 갱신하지 않았으면 false</returns>
	public static async Task<bool> HashSetIfExistsAsync(
		this IDatabase db, LuaScriptLoader scripts, RedisKey key, HashEntry[] fields,
		CancellationToken cancellationToken = default)
	{
		RedisValue[] values = new RedisValue[fields.Length * 2];
		for (Int32 i = 0; i < fields.Length; ++i)
		{
			values[i * 2] = fields[i].Name;
			values[i * 2 + 1] = fields[i].Value;
		}

		RedisResult result = await db.ScriptEvaluateAsync(
			scripts.Load("HashSetIfExistsAtomic"), [key], values).WaitAsync(cancellationToken);
		return (Int64)result == 1;
	}
}
