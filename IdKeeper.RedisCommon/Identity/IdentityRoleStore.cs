using IdKeeper.Database.Redis.Scripts;
using Microsoft.AspNetCore.Identity;
using StackExchange.Redis;

namespace IdKeeper.Database.Redis.Identity;

public sealed class IdentityRoleStore(IConnectionMultiplexer multiplexer, LuaScriptLoader scripts) :
	IRoleStore<IdentityRole>
{
	private IDatabase Db => multiplexer.GetDatabase();

	public void Dispose()
	{
	}

	public Task<string> GetRoleIdAsync(IdentityRole role, CancellationToken cancellationToken) =>
		Task.FromResult(role.Id);

	public Task<string?> GetRoleNameAsync(IdentityRole role, CancellationToken cancellationToken) =>
		Task.FromResult(role.Name);

	public Task SetRoleNameAsync(IdentityRole role, string? roleName, CancellationToken cancellationToken)
	{
		role.Name = roleName;
		return Task.CompletedTask;
	}

	public Task<string?> GetNormalizedRoleNameAsync(IdentityRole role, CancellationToken cancellationToken) =>
		Task.FromResult(role.NormalizedName);

	public Task SetNormalizedRoleNameAsync(
		IdentityRole role, string? normalizedName, CancellationToken cancellationToken)
	{
		role.NormalizedName = normalizedName;
		return Task.CompletedTask;
	}

	public async Task<IdentityResult> CreateAsync(IdentityRole role, CancellationToken cancellationToken)
	{
		if (string.IsNullOrEmpty(role.Id))
		{
			role.Id = Guid.NewGuid().ToString();
		}

		RedisKey[] keys =
		[
			RedisKeyNames.Identity.Role(role.Id),
			RedisKeyNames.Identity.RoleByNormalizedName(role.NormalizedName ?? string.Empty),
		];

		RedisValue[] values =
		[
			role.NormalizedName ?? string.Empty,
			role.Id,
			"Name", role.Name ?? string.Empty,
			"NormalizedName", role.NormalizedName ?? string.Empty,
			"ConcurrencyStamp", role.ConcurrencyStamp ?? string.Empty,
		];

		try
		{
			await Db.ScriptEvaluateAsync(
				scripts.Load("CreateIdentityRoleAtomic"), keys, values).WaitAsync(cancellationToken);
			return IdentityResult.Success;
		}
		catch (RedisServerException ex) when (ex.Message.Contains("DUPLICATE"))
		{
			return IdentityResult.Failed(new IdentityError
			{
				Code = "DuplicateRoleName",
				Description = $"Role name '{role.Name}' is already taken.",
			});
		}
	}

	public async Task<IdentityResult> UpdateAsync(IdentityRole role, CancellationToken cancellationToken)
	{
		await Db.HashSetAsync(RedisKeyNames.Identity.Role(role.Id),
		[
			new("Name", role.Name ?? string.Empty),
			new("NormalizedName", role.NormalizedName ?? string.Empty),
			new("ConcurrencyStamp", role.ConcurrencyStamp ?? string.Empty),
		]);
		return IdentityResult.Success;
	}

	public async Task<IdentityResult> DeleteAsync(IdentityRole role, CancellationToken cancellationToken)
	{
		await Db.KeyDeleteAsync(RedisKeyNames.Identity.Role(role.Id));
		if (!string.IsNullOrEmpty(role.NormalizedName))
		{
			await Db.KeyDeleteAsync(RedisKeyNames.Identity.RoleByNormalizedName(role.NormalizedName));
		}
		return IdentityResult.Success;
	}

	public async Task<IdentityRole?> FindByIdAsync(string roleId, CancellationToken cancellationToken) =>
		await LoadAsync(roleId);

	public async Task<IdentityRole?> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken)
	{
		RedisValue roleId = await Db.StringGetAsync(RedisKeyNames.Identity.RoleByNormalizedName(normalizedRoleName));
		return roleId.IsNullOrEmpty ? null : await LoadAsync(roleId!);
	}

	private async Task<IdentityRole?> LoadAsync(string roleId)
	{
		HashEntry[] entries = await Db.HashGetAllAsync(RedisKeyNames.Identity.Role(roleId));
		if (entries.Length == 0)
		{
			return null;
		}

		Dictionary<string, string> fields = entries.ToDictionary(e => (string)e.Name!, e => (string)e.Value!);
		return new IdentityRole
		{
			Id = roleId,
			Name = fields.GetValueOrDefault("Name"),
			NormalizedName = fields.GetValueOrDefault("NormalizedName"),
			ConcurrencyStamp = fields.GetValueOrDefault("ConcurrencyStamp"),
		};
	}
}
