using IdKeeper.Database.Redis.Extensions;
using IdKeeper.Database.Redis.Models;
using IdKeeper.Database.Redis.Scripts;
using StackExchange.Redis;

namespace IdKeeper.Database.Redis.Repositories;

public sealed class XApiKeyRepository(IConnectionMultiplexer multiplexer, LuaScriptLoader scripts)
{
	private IDatabase Db => multiplexer.GetDatabase();

	public async Task<Int64?> CreateAsync(
		string apiKey, string owner, string? description, DateTime? expiredAtUtc,
		CancellationToken cancellationToken = default)
	{
		RedisKey[] keys =
		[
			RedisKeyNames.XApiKey.ByApiKey(apiKey),
			RedisKeyNames.XApiKey.Seq,
			RedisKeyNames.XApiKey.All,
		];

		RedisValue[] values =
		[
			apiKey,
			owner,
			description ?? string.Empty,
			DateTime.UtcNow.ToUnixSeconds(),
			expiredAtUtc.ToUnixSeconds() ?? RedisValue.EmptyString,
			"IdKeeper/XApiKey/{XApiKey}/",
		];

		try
		{
			RedisResult result = await Db.ScriptEvaluateAsync(
				scripts.Load("CreateXApiKeyAtomic"), keys, values).WaitAsync(cancellationToken);
			return (Int64)result!;
		}
		catch (RedisServerException ex) when (ex.Message.Contains("DUPLICATE_APIKEY"))
		{
			return null;
		}
	}

	public async Task DeleteAsync(Int64 id, CancellationToken cancellationToken = default)
	{
		XApiKey? entry = await GetAsync(id, cancellationToken);
		if (entry is null)
		{
			return;
		}

		RedisKey[] keys =
		[
			RedisKeyNames.XApiKey.Entry(id),
			RedisKeyNames.XApiKey.ByApiKey(entry.ApiKey),
			RedisKeyNames.XApiKey.All,
		];

		await Db.ScriptEvaluateAsync(scripts.Load("DeleteXApiKeyAtomic"), keys, [id]).WaitAsync(cancellationToken);
	}

	public async Task<XApiKey?> FindByApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
	{
		RedisValue id = await Db.StringGetAsync(RedisKeyNames.XApiKey.ByApiKey(apiKey));
		return id.IsNullOrEmpty ? null : await GetAsync((Int64)id, cancellationToken);
	}

	public async Task<bool> UpdateDescriptionAsync(
		Int64 id, string? description, CancellationToken cancellationToken = default)
	{
		RedisKey entryKey = RedisKeyNames.XApiKey.Entry(id);
		if (!await Db.KeyExistsAsync(entryKey))
		{
			return false;
		}

		await Db.HashSetAsync(entryKey, "Description", description ?? string.Empty);
		return true;
	}

	public async Task<bool> UpdateExpiredAtAsync(
		Int64 id, DateTime? expiredAtUtc, CancellationToken cancellationToken = default)
	{
		RedisKey entryKey = RedisKeyNames.XApiKey.Entry(id);
		if (!await Db.KeyExistsAsync(entryKey))
		{
			return false;
		}

		await Db.HashSetAsync(entryKey, "ExpiredAtUtc", expiredAtUtc.ToUnixSeconds() ?? RedisValue.EmptyString);
		return true;
	}

	public async Task<XApiKey?> GetAsync(Int64 id, CancellationToken cancellationToken = default)
	{
		HashEntry[] entries = await Db.HashGetAllAsync(RedisKeyNames.XApiKey.Entry(id));
		if (entries.Length == 0)
		{
			return null;
		}

		Dictionary<string, string> fields = entries.ToDictionary(e => (string)e.Name!, e => (string)e.Value!);
		return new XApiKey
		{
			Id = id,
			ApiKey = fields.GetValueOrDefault("ApiKey", string.Empty),
			Owner = fields.GetValueOrDefault("Owner", string.Empty),
			Description = string.IsNullOrEmpty(fields.GetValueOrDefault("Description")) ? null : fields["Description"],
			CreatedAtUtc = fields["CreatedAtUtc"].ToUtcDateTime(),
			ExpiredAtUtc = fields.GetValueOrDefault("ExpiredAtUtc")?.ToUtcDateTimeOrNull(),
		};
	}

	public async Task<List<XApiKey>> GetAllAsync(CancellationToken cancellationToken = default)
	{
		RedisValue[] ids = await Db.SetMembersAsync(RedisKeyNames.XApiKey.All);
		XApiKey?[] entries = await Task.WhenAll(ids.Select(id => GetAsync((Int64)id, cancellationToken)));
		return [.. entries.Where(e => e is not null).Select(e => e!)];
	}
}
