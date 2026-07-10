using IdKeeper.Database.Redis.Extensions;
using IdKeeper.Database.Redis.Models;
using IdKeeper.Database.Redis.Scripts;
using StackExchange.Redis;

namespace IdKeeper.Database.Redis.Repositories;

public abstract record AllocResult
{
	public sealed record Success(List<Int32> Ids, DateTime ExpiredAtUtc) : AllocResult;
	public sealed record AlreadyExists : AllocResult;
	public sealed record InsufficientIds : AllocResult;
}

public abstract record RenewResult
{
	public sealed record Success(List<Int32> Ids, DateTime ExpiredAtUtc) : RenewResult;
	public sealed record NotFound : RenewResult;
}

public sealed class AllocatedIdRepository(
	IConnectionMultiplexer multiplexer, LuaScriptLoader scripts, AuditLogRepository auditLogRepository)
{
	private IDatabase Db => multiplexer.GetDatabase();

	public async Task<AllocResult> AllocAsync(
		string requester, Int32 count, Int32 maxNodeIdInclusive, TimeSpan firstTimeExpiration,
		string actor, string? remoteIp, string? description, CancellationToken cancellationToken = default)
	{
		DateTime createdAtUtc = DateTime.UtcNow;
		DateTime expiredAtUtc = createdAtUtc.Add(firstTimeExpiration);

		// Bitmap/ByRequester/ExpiryIndex는 {AllocatedId} 해시태그, AuditLog는 {AuditLog}
		// 해시태그라 Redis Cluster에서 같은 슬롯이라는 보장이 없다 — 그래서 감사 로그는
		// 이 EVAL과 한 트랜잭션으로 묶지 않고 성공 후 별도로 기록한다(비원자적, Phase 4
		// CRUD 화면과 동일한 트레이드오프).
		RedisKey[] keys =
		[
			RedisKeyNames.AllocatedId.Bitmap,
			RedisKeyNames.AllocatedId.ByRequester(requester),
			RedisKeyNames.AllocatedId.ExpiryIndex,
		];

		RedisValue[] values =
		[
			maxNodeIdInclusive,
			count,
			Random.Shared.Next(0, maxNodeIdInclusive + 1),
			requester,
			createdAtUtc.ToUnixSeconds(),
			expiredAtUtc.ToUnixSeconds(),
			"IdKeeper/AllocatedId/{AllocatedId}/",
			description ?? string.Empty,
		];

		try
		{
			RedisResult result = await Db.ScriptEvaluateAsync(
				scripts.Load("AllocAtomic"), keys, values).WaitAsync(cancellationToken);

			Int32[] ids = (Int32[])result!;
			await auditLogRepository.AppendAsync(
				"Alloc", actor, requester: requester, affectedIds: ids,
				remoteIp: remoteIp, cancellationToken: cancellationToken);
			return new AllocResult.Success([.. ids], expiredAtUtc);
		}
		catch (RedisServerException ex) when (ex.Message.Contains("ALREADY_EXISTS"))
		{
			return new AllocResult.AlreadyExists();
		}
		catch (RedisServerException ex) when (ex.Message.Contains("INSUFFICIENT_IDS"))
		{
			return new AllocResult.InsufficientIds();
		}
	}

	public async Task<RenewResult> RenewAsync(
		string requester, TimeSpan leaseDuration,
		string actor, string? remoteIp, CancellationToken cancellationToken = default)
	{
		DateTime updatedAtUtc = DateTime.UtcNow;
		DateTime expiredAtUtc = updatedAtUtc.Add(leaseDuration);

		RedisKey[] keys =
		[
			RedisKeyNames.AllocatedId.ByRequester(requester),
			RedisKeyNames.AllocatedId.ExpiryIndex,
		];

		RedisValue[] values =
		[
			"IdKeeper/AllocatedId/{AllocatedId}/",
			updatedAtUtc.ToUnixSeconds(),
			expiredAtUtc.ToUnixSeconds(),
		];

		RedisResult result = await Db.ScriptEvaluateAsync(
			scripts.Load("RenewAtomic"), keys, values).WaitAsync(cancellationToken);

		Int32[] ids = [.. ((RedisValue[])result!).Select(v => (Int32)v)];
		if (ids.Length == 0)
		{
			return new RenewResult.NotFound();
		}

		await auditLogRepository.AppendAsync(
			"Renew", actor, requester: requester, affectedIds: ids,
			remoteIp: remoteIp, cancellationToken: cancellationToken);
		return new RenewResult.Success([.. ids], expiredAtUtc);
	}

	public async Task<List<Int32>> RemoveAsync(
		string requester, string actor, string? remoteIp, CancellationToken cancellationToken = default)
	{
		RedisKey[] keys =
		[
			RedisKeyNames.AllocatedId.Bitmap,
			RedisKeyNames.AllocatedId.ByRequester(requester),
			RedisKeyNames.AllocatedId.ExpiryIndex,
		];

		RedisValue[] values =
		[
			"IdKeeper/AllocatedId/{AllocatedId}/",
		];

		RedisResult result = await Db.ScriptEvaluateAsync(
			scripts.Load("RemoveAtomic"), keys, values).WaitAsync(cancellationToken);

		Int32[] removed = [.. ((RedisValue[])result!).Select(v => (Int32)v)];
		if (removed.Length > 0)
		{
			await auditLogRepository.AppendAsync(
				"Remove", actor, requester: requester, affectedIds: removed,
				remoteIp: remoteIp, cancellationToken: cancellationToken);
		}
		return [.. removed];
	}

	public async Task<bool> ToggleIgnoreExpireAsync(
		Int32 id, bool ignoreExpire, string actor, CancellationToken cancellationToken = default)
	{
		RedisKey[] keys =
		[
			RedisKeyNames.AllocatedId.Entry(id),
			RedisKeyNames.AllocatedId.ExpiryIndex,
		];

		RedisValue[] values =
		[
			id,
			ignoreExpire ? "1" : "0",
		];

		try
		{
			await Db.ScriptEvaluateAsync(
				scripts.Load("ToggleIgnoreExpireAtomic"), keys, values).WaitAsync(cancellationToken);
		}
		catch (RedisServerException ex) when (ex.Message.Contains("NOT_FOUND"))
		{
			return false;
		}

		await auditLogRepository.AppendAsync(
			"IgnoreExpireChanged", actor, affectedIds: [id],
			detail: ignoreExpire ? "enabled" : "disabled", cancellationToken: cancellationToken);
		return true;
	}

	public async Task<bool> UpdateDescriptionAsync(
		Int32 id, string? description, string actor, CancellationToken cancellationToken = default)
	{
		IDatabase db = Db;
		RedisKey entryKey = RedisKeyNames.AllocatedId.Entry(id);
		if (!await db.KeyExistsAsync(entryKey))
		{
			return false;
		}

		await db.HashSetAsync(entryKey, "Description", description ?? string.Empty);
		return true;
	}

	public async Task<Int64> CountOfAllocatedAsync(CancellationToken cancellationToken = default)
	{
		RedisValue bitmap = await Db.StringGetAsync(RedisKeyNames.AllocatedId.Bitmap);
		return CountSetBits(bitmap);
	}

	public async Task<List<AllocatedId>> GetAllAsync(Int32 maxNodeIdInclusive, CancellationToken cancellationToken = default)
	{
		RedisValue raw = await Db.StringGetAsync(RedisKeyNames.AllocatedId.Bitmap);
		byte[] bytes = raw.IsNullOrEmpty ? [] : (byte[])raw!;

		List<Int32> allocatedIds = [];
		for (Int32 id = 0; id <= maxNodeIdInclusive; ++id)
		{
			Int32 byteIndex = id / 8;
			if (byteIndex >= bytes.Length)
			{
				break;
			}
			Int32 bitOffset = 7 - (id % 8);
			if (((bytes[byteIndex] >> bitOffset) & 1) == 1)
			{
				allocatedIds.Add(id);
			}
		}

		AllocatedId?[] entries = await Task.WhenAll(allocatedIds.Select(id => GetAsync(id, cancellationToken)));
		return [.. entries.Where(e => e is not null).Select(e => e!)];
	}

	public async Task<AllocatedId?> GetAsync(Int32 id, CancellationToken cancellationToken = default)
	{
		HashEntry[] entries = await Db.HashGetAllAsync(RedisKeyNames.AllocatedId.Entry(id));
		if (entries.Length == 0)
		{
			return null;
		}

		Dictionary<string, string> fields = entries.ToDictionary(e => (string)e.Name!, e => (string)e.Value!);
		return new AllocatedId
		{
			Id = id,
			Requester = fields.GetValueOrDefault("Requester", string.Empty),
			CreatedAtUtc = fields["CreatedAtUtc"].ToUtcDateTime(),
			UpdatedAtUtc = fields.GetValueOrDefault("UpdatedAtUtc")?.ToUtcDateTimeOrNull(),
			ExpiredAtUtc = fields["ExpiredAtUtc"].ToUtcDateTime(),
			IgnoreExpire = fields.GetValueOrDefault("IgnoreExpire") == "1",
			Description = string.IsNullOrEmpty(fields.GetValueOrDefault("Description")) ? null : fields["Description"],
		};
	}

	private static Int64 CountSetBits(RedisValue bitmap)
	{
		if (bitmap.IsNullOrEmpty)
		{
			return 0;
		}

		byte[] bytes = (byte[])bitmap!;
		Int64 count = 0;
		foreach (byte b in bytes)
		{
			count += System.Numerics.BitOperations.PopCount(b);
		}
		return count;
	}
}
