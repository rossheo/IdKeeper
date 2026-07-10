using IdKeeper.Database.Redis.Models;
using IdKeeper.Database.Redis.Scripts;
using StackExchange.Redis;

namespace IdKeeper.Database.Redis.Repositories;

public sealed class XApiAllowedHostnameRepository(IConnectionMultiplexer multiplexer, LuaScriptLoader scripts)
{
	private IDatabase Db => multiplexer.GetDatabase();

	public async Task<bool> CreateAsync(string hostname, string? description, CancellationToken cancellationToken = default)
	{
		RedisKey[] keys = [RedisKeyNames.XApiAllowedHostname.Entry(hostname), RedisKeyNames.XApiAllowedHostname.All];
		RedisValue[] values = [hostname, "Description", description ?? string.Empty];

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

	public async Task DeleteAsync(string hostname, CancellationToken cancellationToken = default)
	{
		RedisKey[] keys = [RedisKeyNames.XApiAllowedHostname.Entry(hostname), RedisKeyNames.XApiAllowedHostname.All];
		await Db.ScriptEvaluateAsync(
			scripts.Load("DeleteNaturalKeyEntityAtomic"), keys, [hostname]).WaitAsync(cancellationToken);
	}

	public async Task<bool> ExistsAsync(string hostname, CancellationToken cancellationToken = default) =>
		await Db.KeyExistsAsync(RedisKeyNames.XApiAllowedHostname.Entry(hostname));

	public async Task<bool> UpdateDescriptionAsync(
		string hostname, string? description, CancellationToken cancellationToken = default)
	{
		RedisKey entryKey = RedisKeyNames.XApiAllowedHostname.Entry(hostname);
		if (!await Db.KeyExistsAsync(entryKey))
		{
			return false;
		}

		await Db.HashSetAsync(entryKey, "Description", description ?? string.Empty);
		return true;
	}

	public async Task<List<XApiAllowedHostname>> GetAllAsync(CancellationToken cancellationToken = default)
	{
		RedisValue[] hostnames = await Db.SetMembersAsync(RedisKeyNames.XApiAllowedHostname.All);
		List<XApiAllowedHostname> result = [];
		foreach (RedisValue hostname in hostnames)
		{
			RedisValue description =
				await Db.HashGetAsync(RedisKeyNames.XApiAllowedHostname.Entry(hostname!), "Description");
			result.Add(new XApiAllowedHostname
			{
				Hostname = hostname!,
				Description = description.IsNullOrEmpty ? null : (string?)description,
			});
		}
		return result;
	}
}
