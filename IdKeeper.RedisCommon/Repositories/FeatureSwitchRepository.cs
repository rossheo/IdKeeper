using IdKeeper.Database.Redis.Models;
using IdKeeper.Database.Redis.Scripts;
using StackExchange.Redis;

namespace IdKeeper.Database.Redis.Repositories;

public sealed class FeatureSwitchRepository(IConnectionMultiplexer multiplexer, LuaScriptLoader scripts)
{
	private IDatabase Db => multiplexer.GetDatabase();

	public async Task<bool> CreateAsync(
		string key, bool isEnabled, string? description, CancellationToken cancellationToken = default)
	{
		RedisKey[] keys = [RedisKeyNames.FeatureSwitch.Entry(key), RedisKeyNames.FeatureSwitch.All];
		RedisValue[] values =
		[
			key,
			"IsEnabled", isEnabled ? "1" : "0",
			"Description", description ?? string.Empty,
		];

		try
		{
			await Db.ScriptEvaluateAsync(
				scripts.Load("CreateNaturalKeyEntityAtomic"), keys, values).WaitAsync(cancellationToken);
			return true;
		}
		catch (RedisServerException ex) when (ex.Message.Contains("DUPLICATE"))
		{
			return false;
		}
	}

	public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
		await Db.KeyExistsAsync(RedisKeyNames.FeatureSwitch.Entry(key));

	public async Task<bool> UpdateAsync(
		string key, bool isEnabled, string? description, CancellationToken cancellationToken = default)
	{
		RedisKey entryKey = RedisKeyNames.FeatureSwitch.Entry(key);
		if (!await Db.KeyExistsAsync(entryKey))
		{
			return false;
		}

		await Db.HashSetAsync(entryKey,
		[
			new("IsEnabled", isEnabled ? "1" : "0"),
			new("Description", description ?? string.Empty),
		]);
		return true;
	}

	public async Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
		await Db.ScriptEvaluateAsync(
			scripts.Load("DeleteNaturalKeyEntityAtomic"),
			[RedisKeyNames.FeatureSwitch.Entry(key), RedisKeyNames.FeatureSwitch.All],
			[key]);

	public async Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default)
	{
		RedisValue value = await Db.HashGetAsync(RedisKeyNames.FeatureSwitch.Entry(key), "IsEnabled");
		return value == "1";
	}

	public async Task<List<FeatureSwitch>> GetAllAsync(CancellationToken cancellationToken = default)
	{
		RedisValue[] keys = await Db.SetMembersAsync(RedisKeyNames.FeatureSwitch.All);
		List<FeatureSwitch> result = [];
		foreach (RedisValue key in keys)
		{
			HashEntry[] entries = await Db.HashGetAllAsync(RedisKeyNames.FeatureSwitch.Entry(key!));
			Dictionary<string, string> fields = entries.ToDictionary(e => (string)e.Name!, e => (string)e.Value!);
			result.Add(new FeatureSwitch
			{
				Key = key!,
				IsEnabled = fields.GetValueOrDefault("IsEnabled") == "1",
				Description = string.IsNullOrEmpty(fields.GetValueOrDefault("Description")) ? null : fields["Description"],
			});
		}
		return result;
	}
}
