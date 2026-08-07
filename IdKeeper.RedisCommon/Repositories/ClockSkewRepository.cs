using IdKeeper.Database.Redis.Extensions;
using IdKeeper.Database.Redis.Models;
using StackExchange.Redis;

namespace IdKeeper.Database.Redis.Repositories;

/// <summary>
/// requester별 마지막 클라이언트 시계 오차 관측값을 보관한다. 진단용이라 Lua/EVAL을 쓰지 않고
/// 감사 로그도 남기지 않는다.
/// </summary>
public sealed class ClockSkewRepository(IConnectionMultiplexer multiplexer)
{
	private IDatabase Db => multiplexer.GetDatabase();

	/// <summary>
	/// 관측값을 기록한다. TTL은 호출부가 넘긴다 — 이 프로젝트는 ApiService 설정
	/// (LeaseDuration/CleanupGracePeriod)을 참조할 수 없기 때문이며,
	/// AllocAsync가 firstTimeExpiration을 받는 것과 같은 이유다.
	/// </summary>
	public async Task RecordAsync(
		string requester, TimeSpan skew, string operation, ClockSkewSeverity severity, string? remoteIp,
		TimeSpan ttl, CancellationToken cancellationToken = default)
	{
		RedisKey key = RedisKeyNames.ClockSkew.Entry(requester);

		// HSET은 기존 키의 TTL을 지우지 않으므로, KeyExpire가 유실되어도 다음 갱신에 다시
		// 적용되어 자가 복구된다. 세 명령을 한 번의 왕복으로 묶는다.
		IBatch batch = Db.CreateBatch();
		Task hashTask = batch.HashSetAsync(key,
		[
			new("SkewSeconds", (Int64)skew.TotalSeconds),
			new("ObservedAtUtc", DateTime.UtcNow.ToUnixSeconds()),
			new("Operation", operation),
			new("Severity", severity.ToString()),
			new("RemoteIp", remoteIp ?? string.Empty),
		]);
		Task expireTask = batch.KeyExpireAsync(key, ttl);
		Task indexTask = batch.SetAddAsync(RedisKeyNames.ClockSkew.All, requester);
		batch.Execute();

		await Task.WhenAll(hashTask, expireTask, indexTask).WaitAsync(cancellationToken);
	}

	public async Task<ClockSkewObservation?> GetAsync(
		string requester, CancellationToken cancellationToken = default)
	{
		HashEntry[] entries =
			await Db.HashGetAllAsync(RedisKeyNames.ClockSkew.Entry(requester)).WaitAsync(cancellationToken);
		if (entries.Length == 0)
		{
			return null;
		}

		Dictionary<string, string> fields =
			entries.ToDictionary(e => (string)e.Name!, e => (string)e.Value!);

		return new ClockSkewObservation
		{
			Requester = requester,
			SkewSeconds = Int64.TryParse(fields.GetValueOrDefault("SkewSeconds"), out Int64 skew) ? skew : 0,
			ObservedAtUtc = fields["ObservedAtUtc"].ToUtcDateTime(),
			Operation = fields.GetValueOrDefault("Operation", string.Empty),
			Severity = ParseSeverity(fields),
			RemoteIp = string.IsNullOrEmpty(fields.GetValueOrDefault("RemoteIp"))
				? null
				: fields["RemoteIp"],
		};
	}

	// Severity 도입 이전에 기록된 항목은 Rejected 불리언만 갖고 있다. 그 기록들도 TTL로
	// 곧 사라지지만, 그때까지 화면·알림에서 등급이 뒤바뀌지 않도록 대체 경로를 둔다.
	private static ClockSkewSeverity ParseSeverity(Dictionary<string, string> fields)
	{
		if (fields.TryGetValue("Severity", out string? raw)
			&& Enum.TryParse(raw, out ClockSkewSeverity severity))
		{
			return severity;
		}

		return fields.GetValueOrDefault("Rejected") == "1"
			? ClockSkewSeverity.Reject
			: ClockSkewSeverity.None;
	}

	/// <summary>
	/// 여러 requester의 관측값을 한 번에 조회한다. 기록이 없는 requester는 결과에서 빠진다.
	/// AllocatedIdRepository.GetAllAsync와 동일한 팬아웃 형태다 (SCAN을 쓰지 않는다).
	/// </summary>
	public async Task<Dictionary<string, ClockSkewObservation>> GetManyAsync(
		IEnumerable<string> requesters, CancellationToken cancellationToken = default)
	{
		string[] distinct = [.. requesters.Distinct()];
		ClockSkewObservation?[] observations =
			await Task.WhenAll(distinct.Select(r => GetAsync(r, cancellationToken)));

		Dictionary<string, ClockSkewObservation> result = [];
		for (Int32 i = 0; i < distinct.Length; ++i)
		{
			if (observations[i] is not null)
			{
				result[distinct[i]] = observations[i]!;
			}
		}
		return result;
	}

	/// <summary>
	/// 인덱스에 등록된 모든 관측값을 조회한다. 노드 Id를 받지 못한(= Alloc이 거부된)
	/// 클라이언트도 포함되므로 알림에는 이 경로를 쓴다.
	/// 엔트리가 TTL로 사라진 requester는 인덱스에서 함께 제거한다 — 인덱스가 무한히
	/// 커지지 않도록 조회 시점에 정리하는 방식이다.
	/// </summary>
	public async Task<List<ClockSkewObservation>> GetAllAsync(
		CancellationToken cancellationToken = default)
	{
		RedisValue[] members =
			await Db.SetMembersAsync(RedisKeyNames.ClockSkew.All).WaitAsync(cancellationToken);
		if (members.Length == 0)
		{
			return [];
		}

		string[] requesters = [.. members.Select(m => (string)m!)];
		ClockSkewObservation?[] observations =
			await Task.WhenAll(requesters.Select(r => GetAsync(r, cancellationToken)));

		List<ClockSkewObservation> result = [];
		List<RedisValue> stale = [];
		for (Int32 i = 0; i < requesters.Length; ++i)
		{
			if (observations[i] is null)
			{
				stale.Add(requesters[i]);
			}
			else
			{
				result.Add(observations[i]!);
			}
		}

		if (stale.Count > 0)
		{
			// 정리 실패가 조회를 실패시키지 않도록 fire-and-forget으로 처리한다.
			await Db.SetRemoveAsync(RedisKeyNames.ClockSkew.All, [.. stale], CommandFlags.FireAndForget);
		}

		return result;
	}
}
