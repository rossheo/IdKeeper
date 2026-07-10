using IdKeeper.Database.Redis.Models;
using IdKeeper.Database.Redis.Scripts;
using StackExchange.Redis;

namespace IdKeeper.Database.Redis.Repositories;

public sealed class XApiAllowedCidrRepository(IConnectionMultiplexer multiplexer, LuaScriptLoader scripts)
{
	private IDatabase Db => multiplexer.GetDatabase();

	public async Task<bool> CreateAsync(string cidr, string? description, CancellationToken cancellationToken = default)
	{
		RedisKey[] keys = [RedisKeyNames.XApiAllowedCidr.Entry(cidr), RedisKeyNames.XApiAllowedCidr.All];
		RedisValue[] values = [cidr, "Description", description ?? string.Empty];

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

	public async Task DeleteAsync(string cidr, CancellationToken cancellationToken = default)
	{
		RedisKey[] keys = [RedisKeyNames.XApiAllowedCidr.Entry(cidr), RedisKeyNames.XApiAllowedCidr.All];
		await Db.ScriptEvaluateAsync(
			scripts.Load("DeleteNaturalKeyEntityAtomic"), keys, [cidr]).WaitAsync(cancellationToken);
	}

	public async Task<bool> ExistsAsync(string cidr, CancellationToken cancellationToken = default) =>
		await Db.KeyExistsAsync(RedisKeyNames.XApiAllowedCidr.Entry(cidr));

	public async Task<bool> UpdateDescriptionAsync(
		string cidr, string? description, CancellationToken cancellationToken = default)
	{
		RedisKey entryKey = RedisKeyNames.XApiAllowedCidr.Entry(cidr);
		if (!await Db.KeyExistsAsync(entryKey))
		{
			return false;
		}

		await Db.HashSetAsync(entryKey, "Description", description ?? string.Empty);
		return true;
	}

	public async Task<List<XApiAllowedCidr>> GetAllAsync(CancellationToken cancellationToken = default)
	{
		RedisValue[] cidrs = await Db.SetMembersAsync(RedisKeyNames.XApiAllowedCidr.All);
		List<XApiAllowedCidr> result = [];
		foreach (RedisValue cidr in cidrs)
		{
			RedisValue description = await Db.HashGetAsync(RedisKeyNames.XApiAllowedCidr.Entry(cidr!), "Description");
			result.Add(new XApiAllowedCidr
			{
				Cidr = cidr!,
				Description = description.IsNullOrEmpty ? null : (string?)description,
			});
		}
		return result;
	}
}
