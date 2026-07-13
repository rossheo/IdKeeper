using System.Net;
using System.Text;
using System.Text.Json;
using StackExchange.Redis;

namespace IdKeeper.Database.Redis.Backup;

/// <summary>
/// IdKeeper/* 프리픽스 전체를 대상으로 하는 Export/Import(백업/이관) 도구.
/// DUMP/RESTORE로 타입(Bitmap/Hash/Set/ZSET/String) 불문 Redis 자체 직렬화 포맷을 그대로
/// 재사용한다 — 엔티티별 커스텀 직렬화 코드가 필요 없다.
/// </summary>
public sealed class RedisBackupService(IConnectionMultiplexer multiplexer)
{
	private const Int32 ScanPageSize = 500;

	public async Task<Int32> ExportAsync(Stream output, CancellationToken cancellationToken = default)
	{
		IDatabase db = multiplexer.GetDatabase();
		await using StreamWriter writer = new(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
		Int32 count = 0;

		await foreach (RedisKey key in EnumerateKeysAsync(cancellationToken))
		{
			byte[]? dump = await db.KeyDumpAsync(key);
			if (dump is null)
			{
				// SCAN과 DUMP 사이 레이스로 그새 삭제된 키 — 건너뛴다.
				continue;
			}

			TimeSpan? ttl = await db.KeyTimeToLiveAsync(key);
			BackupEntry entry = new(key.ToString(), (Int64)(ttl?.TotalMilliseconds ?? 0), Convert.ToBase64String(dump));
			await writer.WriteLineAsync(JsonSerializer.Serialize(entry).AsMemory(), cancellationToken);
			++count;
		}

		await writer.FlushAsync(cancellationToken);
		return count;
	}

	/// <summary>
	/// 기본은 파일에 있는 키만 REPLACE(그 외 기존 키는 유지). purgeBeforeImport=true면
	/// 가져오기 전 IdKeeper/* 전체를 삭제해 다른 Redis로의 완전한 미러링을 지원한다.
	/// </summary>
	public async Task<Int32> ImportAsync(
		Stream input, bool purgeBeforeImport, CancellationToken cancellationToken = default)
	{
		IDatabase db = multiplexer.GetDatabase();

		if (purgeBeforeImport)
		{
			await PurgeAsync(cancellationToken);
		}

		using StreamReader reader = new(input, Encoding.UTF8, leaveOpen: true);
		Int32 count = 0;
		string? line;
		while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
		{
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			BackupEntry? entry = JsonSerializer.Deserialize<BackupEntry>(line);
			if (entry is null)
			{
				continue;
			}

			byte[] payload = Convert.FromBase64String(entry.Payload);
			// StackExchange.Redis의 KeyRestoreAsync는 REPLACE 옵션을 지원하지 않아
			// RESTORE를 직접 실행한다(ttl=0은 "만료 없음"으로 원본 TTL 부재 상태를 그대로 복원).
			await db.ExecuteAsync("RESTORE", (RedisKey)entry.Key, entry.TtlMs, payload, "REPLACE");
			++count;
		}

		return count;
	}

	public async Task<Int32> CountKeysAsync(CancellationToken cancellationToken = default)
	{
		Int32 count = 0;
		await foreach (RedisKey _ in EnumerateKeysAsync(cancellationToken))
		{
			++count;
		}
		return count;
	}

	/// <summary>
	/// 새 Redis로 실제 마이그레이션을 실행하기 전, 접속 가능한지만 확인한다(PING).
	/// </summary>
	public static async Task<(bool Success, string? Error)> TestConnectionAsync(
		string destinationConnectionString, CancellationToken cancellationToken = default)
	{
		try
		{
			await using ConnectionMultiplexer destination =
				await ConnectionMultiplexer.ConnectAsync(destinationConnectionString);
			await destination.GetDatabase().PingAsync();
			return (true, null);
		}
		catch (Exception ex)
		{
			return (false, ex.Message);
		}
	}

	/// <summary>
	/// 현재 연결된 Redis의 "IdKeeper/*" 전체를 다른 Redis로 이관한다. DUMP/RESTORE(RDB
	/// 직렬화)는 서로 다른 Redis 버전 간 호환되지 않을 수 있어(예: Redis 7.4가 도입한
	/// RDB 버전 12는 7.0 이하가 거부) 타입별 네이티브 커맨드(GET/HGETALL/SMEMBERS/
	/// ZRANGE/LRANGE → SET/HSET/SADD/ZADD/RPUSH)로 값을 그대로 읽고 쓴다.
	/// </summary>
	public async Task<Int32> MigrateAsync(
		string destinationConnectionString, bool purgeBeforeMigrate, CancellationToken cancellationToken = default)
	{
		await using ConnectionMultiplexer destination =
			await ConnectionMultiplexer.ConnectAsync(destinationConnectionString);
		IDatabase destinationDb = destination.GetDatabase();

		if (purgeBeforeMigrate)
		{
			foreach (EndPoint endpoint in destination.GetEndPoints())
			{
				IServer destinationServer = destination.GetServer(endpoint);
				if (destinationServer.IsReplica)
				{
					continue;
				}

				await foreach (RedisKey key in destinationServer.KeysAsync(
					pattern: RedisKeyNames.AllKeysPattern, pageSize: ScanPageSize).WithCancellation(cancellationToken))
				{
					await destinationDb.KeyDeleteAsync(key);
				}
			}
		}

		IDatabase sourceDb = multiplexer.GetDatabase();
		Int32 count = 0;

		await foreach (RedisKey key in EnumerateKeysAsync(cancellationToken))
		{
			RedisType type = await sourceDb.KeyTypeAsync(key);
			TimeSpan? ttl = await sourceDb.KeyTimeToLiveAsync(key);

			switch (type)
			{
				case RedisType.String:
					RedisValue str = await sourceDb.StringGetAsync(key);
					await destinationDb.StringSetAsync(key, str);
					break;

				case RedisType.Hash:
					HashEntry[] hash = await sourceDb.HashGetAllAsync(key);
					if (hash.Length > 0)
					{
						await destinationDb.HashSetAsync(key, hash);
					}
					break;

				case RedisType.Set:
					RedisValue[] set = await sourceDb.SetMembersAsync(key);
					if (set.Length > 0)
					{
						await destinationDb.SetAddAsync(key, set);
					}
					break;

				case RedisType.SortedSet:
					SortedSetEntry[] zset = await sourceDb.SortedSetRangeByScoreWithScoresAsync(key);
					if (zset.Length > 0)
					{
						await destinationDb.SortedSetAddAsync(key, zset);
					}
					break;

				case RedisType.List:
					RedisValue[] list = await sourceDb.ListRangeAsync(key, 0, -1);
					if (list.Length > 0)
					{
						await destinationDb.ListRightPushAsync(key, list);
					}
					break;

				default:
					// SCAN과 조회 사이 레이스로 그새 삭제된 키 — 건너뛴다.
					continue;
			}

			if (ttl.HasValue)
			{
				await destinationDb.KeyExpireAsync(key, ttl.Value);
			}

			++count;
		}

		return count;
	}

	private async Task PurgeAsync(CancellationToken cancellationToken)
	{
		IDatabase db = multiplexer.GetDatabase();
		await foreach (RedisKey key in EnumerateKeysAsync(cancellationToken))
		{
			await db.KeyDeleteAsync(key);
		}
	}

	/// <summary>
	/// 각 마스터 노드마다 개별적으로 SCAN한다 — 단일 인스턴스에서는 노드가 1개뿐이라
	/// 자연히 동일하게 동작하고, 향후 Redis Cluster로 확장해도 코드 변경 없이 재사용된다.
	/// </summary>
	private async IAsyncEnumerable<RedisKey> EnumerateKeysAsync(
		[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
	{
		foreach (EndPoint endpoint in multiplexer.GetEndPoints())
		{
			IServer server = multiplexer.GetServer(endpoint);
			if (server.IsReplica)
			{
				continue;
			}

			await foreach (RedisKey key in server.KeysAsync(
				pattern: RedisKeyNames.AllKeysPattern, pageSize: ScanPageSize).WithCancellation(cancellationToken))
			{
				yield return key;
			}
		}
	}

	private sealed record BackupEntry(string Key, Int64 TtlMs, string Payload);
}
