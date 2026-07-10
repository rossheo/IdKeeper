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
