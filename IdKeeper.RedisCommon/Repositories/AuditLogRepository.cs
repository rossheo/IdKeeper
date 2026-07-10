using IdKeeper.Database.Redis.Extensions;
using IdKeeper.Database.Redis.Models;
using StackExchange.Redis;

namespace IdKeeper.Database.Redis.Repositories;

public sealed class AuditLogRepository(IConnectionMultiplexer multiplexer)
{
	private IDatabase Db => multiplexer.GetDatabase();

	public async Task AppendAsync(
		string action, string actor, string? requester = null, IEnumerable<Int32>? affectedIds = null,
		string? remoteIp = null, string? detail = null, CancellationToken cancellationToken = default)
	{
		DateTime nowUtc = DateTime.UtcNow;
		Int64 id = await Db.StringIncrementAsync(RedisKeyNames.AuditLog.Seq);

		HashEntry[] fields =
		[
			new("Action", action),
			new("Actor", actor),
			new("Requester", requester ?? string.Empty),
			new("AffectedIds", affectedIds is null ? string.Empty : string.Join(",", affectedIds)),
			new("RemoteIp", remoteIp ?? string.Empty),
			new("Detail", detail ?? string.Empty),
			new("CreatedAtUtc", nowUtc.ToUnixSeconds()),
		];

		await Db.HashSetAsync(RedisKeyNames.AuditLog.Entry(id), fields);
		await Db.SortedSetAddAsync(RedisKeyNames.AuditLog.All, id, nowUtc.ToUnixSeconds());
	}

	/// <summary>
	/// 시간 역순으로 최근 항목부터 조회 후 애플리케이션 메모리에서 Action(완전일치)/
	/// Actor·Requester(부분일치) 필터를 적용한다. 90일 보존 정책으로 규모가 제한되어
	/// 별도 인덱스 없이도 허용 가능한 트레이드오프로 판단(계획 문서 참조).
	/// </summary>
	public async Task<(List<AuditLog> Items, Int32 TotalCount)> QueryAsync(
		string? actionFilter, string? actorFilter, string? requesterFilter,
		Int32 page, Int32 pageSize, CancellationToken cancellationToken = default)
	{
		RedisValue[] allIds = await Db.SortedSetRangeByScoreAsync(
			RedisKeyNames.AuditLog.All, order: Order.Descending);

		List<AuditLog> matched = [];
		foreach (RedisValue idValue in allIds)
		{
			AuditLog? entry = await GetAsync((Int64)idValue, cancellationToken);
			if (entry is null)
			{
				continue;
			}

			if (actionFilter is not null && entry.Action != actionFilter)
			{
				continue;
			}
			if (!string.IsNullOrEmpty(actorFilter) &&
				!entry.Actor.Contains(actorFilter, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			if (!string.IsNullOrEmpty(requesterFilter) &&
				(entry.Requester is null ||
					!entry.Requester.Contains(requesterFilter, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}

			matched.Add(entry);
		}

		List<AuditLog> pageItems = [.. matched.Skip(page * pageSize).Take(pageSize)];
		return (pageItems, matched.Count);
	}

	public async Task<AuditLog?> GetAsync(Int64 id, CancellationToken cancellationToken = default)
	{
		HashEntry[] entries = await Db.HashGetAllAsync(RedisKeyNames.AuditLog.Entry(id));
		if (entries.Length == 0)
		{
			return null;
		}

		Dictionary<string, string> fields = entries.ToDictionary(e => (string)e.Name!, e => (string)e.Value!);
		return new AuditLog
		{
			Id = id,
			Action = fields.GetValueOrDefault("Action", string.Empty),
			Actor = fields.GetValueOrDefault("Actor", string.Empty),
			Requester = string.IsNullOrEmpty(fields.GetValueOrDefault("Requester")) ? null : fields["Requester"],
			AffectedIds = string.IsNullOrEmpty(fields.GetValueOrDefault("AffectedIds")) ? null : fields["AffectedIds"],
			RemoteIp = string.IsNullOrEmpty(fields.GetValueOrDefault("RemoteIp")) ? null : fields["RemoteIp"],
			Detail = string.IsNullOrEmpty(fields.GetValueOrDefault("Detail")) ? null : fields["Detail"],
			CreatedAtUtc = fields["CreatedAtUtc"].ToUtcDateTime(),
		};
	}

	/// <summary>90일 이상 지난 로그를 청크 단위로 삭제한다(CleanupAuditLogJob).</summary>
	public async Task<Int32> DeleteOlderThanAsync(
		DateTime cutoffUtc, Int32 batchSize = 200, CancellationToken cancellationToken = default)
	{
		Int64 cutoffUnix = cutoffUtc.ToUnixSeconds();
		Int32 totalDeleted = 0;

		while (true)
		{
			RedisValue[] ids = await Db.SortedSetRangeByScoreAsync(
				RedisKeyNames.AuditLog.All, double.NegativeInfinity, cutoffUnix, take: batchSize);
			if (ids.Length == 0)
			{
				break;
			}

			foreach (RedisValue id in ids)
			{
				await Db.KeyDeleteAsync(RedisKeyNames.AuditLog.Entry((Int64)id));
				await Db.SortedSetRemoveAsync(RedisKeyNames.AuditLog.All, id);
			}

			totalDeleted += ids.Length;
			if (ids.Length < batchSize)
			{
				break;
			}
		}

		return totalDeleted;
	}
}
