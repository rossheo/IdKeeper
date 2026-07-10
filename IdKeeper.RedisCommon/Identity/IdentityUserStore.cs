using IdKeeper.Database.Redis.Extensions;
using IdKeeper.Database.Redis.Scripts;
using Microsoft.AspNetCore.Identity;
using StackExchange.Redis;

namespace IdKeeper.Database.Redis.Identity;

public sealed class IdentityUserStore(IConnectionMultiplexer multiplexer, LuaScriptLoader scripts) :
	IUserStore<IdentityUser>,
	IUserEmailStore<IdentityUser>,
	IUserPasswordStore<IdentityUser>,
	IUserLockoutStore<IdentityUser>,
	IUserRoleStore<IdentityUser>,
	IUserSecurityStampStore<IdentityUser>
{
	private IDatabase Db => multiplexer.GetDatabase();

	public void Dispose()
	{
	}

	#region IUserStore

	public Task<string> GetUserIdAsync(IdentityUser user, CancellationToken cancellationToken) =>
		Task.FromResult(user.Id);

	public Task<string?> GetUserNameAsync(IdentityUser user, CancellationToken cancellationToken) =>
		Task.FromResult(user.UserName);

	public Task SetUserNameAsync(IdentityUser user, string? userName, CancellationToken cancellationToken)
	{
		user.UserName = userName;
		return Task.CompletedTask;
	}

	public Task<string?> GetNormalizedUserNameAsync(IdentityUser user, CancellationToken cancellationToken) =>
		Task.FromResult(user.NormalizedUserName);

	public Task SetNormalizedUserNameAsync(
		IdentityUser user, string? normalizedName, CancellationToken cancellationToken)
	{
		user.NormalizedUserName = normalizedName;
		return Task.CompletedTask;
	}

	public async Task<IdentityResult> CreateAsync(IdentityUser user, CancellationToken cancellationToken)
	{
		if (string.IsNullOrEmpty(user.Id))
		{
			user.Id = Guid.NewGuid().ToString();
		}

		RedisKey[] keys =
		[
			RedisKeyNames.Identity.User(user.Id),
			RedisKeyNames.Identity.UserByNormalizedUserName(user.NormalizedUserName ?? string.Empty),
			RedisKeyNames.Identity.UserByNormalizedEmail(user.NormalizedEmail ?? string.Empty),
			RedisKeyNames.Identity.UserAll,
		];

		RedisValue[] values =
		[
			user.Id,
			user.NormalizedUserName ?? string.Empty,
			user.NormalizedEmail ?? string.Empty,
			.. ToHashPairs(user),
		];

		try
		{
			await Db.ScriptEvaluateAsync(
				scripts.Load("CreateIdentityUserAtomic"), keys, values).WaitAsync(cancellationToken);
			return IdentityResult.Success;
		}
		catch (RedisServerException ex) when (ex.Message.Contains("DUPLICATE_USERNAME"))
		{
			return IdentityResult.Failed(new IdentityError
			{
				Code = "DuplicateUserName",
				Description = $"UserName '{user.UserName}' is already taken.",
			});
		}
		catch (RedisServerException ex) when (ex.Message.Contains("DUPLICATE_EMAIL"))
		{
			return IdentityResult.Failed(new IdentityError
			{
				Code = "DuplicateEmail",
				Description = $"Email '{user.Email}' is already taken.",
			});
		}
	}

	public async Task<IdentityResult> UpdateAsync(IdentityUser user, CancellationToken cancellationToken)
	{
		RedisKey entryKey = RedisKeyNames.Identity.User(user.Id);
		HashEntry[] existing = await Db.HashGetAllAsync(entryKey);
		if (existing.Length == 0)
		{
			return IdentityResult.Failed(new IdentityError { Code = "NotFound", Description = "User not found." });
		}

		Dictionary<string, string> fields = existing.ToDictionary(e => (string)e.Name!, e => (string)e.Value!);
		string? oldNormalizedUserName = fields.GetValueOrDefault("NormalizedUserName");
		string? oldNormalizedEmail = fields.GetValueOrDefault("NormalizedEmail");

		bool userNameChanged = oldNormalizedUserName != (user.NormalizedUserName ?? string.Empty);
		bool emailChanged = oldNormalizedEmail != (user.NormalizedEmail ?? string.Empty);

		if (userNameChanged && !string.IsNullOrEmpty(user.NormalizedUserName) &&
			await Db.KeyExistsAsync(RedisKeyNames.Identity.UserByNormalizedUserName(user.NormalizedUserName)))
		{
			return IdentityResult.Failed(new IdentityError
			{
				Code = "DuplicateUserName",
				Description = $"UserName '{user.UserName}' is already taken.",
			});
		}

		if (emailChanged && !string.IsNullOrEmpty(user.NormalizedEmail) &&
			await Db.KeyExistsAsync(RedisKeyNames.Identity.UserByNormalizedEmail(user.NormalizedEmail)))
		{
			return IdentityResult.Failed(new IdentityError
			{
				Code = "DuplicateEmail",
				Description = $"Email '{user.Email}' is already taken.",
			});
		}

		await Db.HashSetAsync(entryKey, ToHashEntries(user));

		if (userNameChanged)
		{
			if (!string.IsNullOrEmpty(oldNormalizedUserName))
			{
				await Db.KeyDeleteAsync(RedisKeyNames.Identity.UserByNormalizedUserName(oldNormalizedUserName));
			}
			if (!string.IsNullOrEmpty(user.NormalizedUserName))
			{
				await Db.StringSetAsync(
					RedisKeyNames.Identity.UserByNormalizedUserName(user.NormalizedUserName), user.Id);
			}
		}

		if (emailChanged)
		{
			if (!string.IsNullOrEmpty(oldNormalizedEmail))
			{
				await Db.KeyDeleteAsync(RedisKeyNames.Identity.UserByNormalizedEmail(oldNormalizedEmail));
			}
			if (!string.IsNullOrEmpty(user.NormalizedEmail))
			{
				await Db.StringSetAsync(RedisKeyNames.Identity.UserByNormalizedEmail(user.NormalizedEmail), user.Id);
			}
		}

		return IdentityResult.Success;
	}

	public async Task<IdentityResult> DeleteAsync(IdentityUser user, CancellationToken cancellationToken)
	{
		RedisValue[] roles = await Db.SetMembersAsync(RedisKeyNames.Identity.UserRoles(user.Id));
		foreach (RedisValue role in roles)
		{
			await Db.SetRemoveAsync(RedisKeyNames.Identity.RoleUsers(role!), user.Id);
		}

		await Db.KeyDeleteAsync(RedisKeyNames.Identity.UserRoles(user.Id));
		if (!string.IsNullOrEmpty(user.NormalizedUserName))
		{
			await Db.KeyDeleteAsync(RedisKeyNames.Identity.UserByNormalizedUserName(user.NormalizedUserName));
		}
		if (!string.IsNullOrEmpty(user.NormalizedEmail))
		{
			await Db.KeyDeleteAsync(RedisKeyNames.Identity.UserByNormalizedEmail(user.NormalizedEmail));
		}
		await Db.KeyDeleteAsync(RedisKeyNames.Identity.User(user.Id));
		await Db.SetRemoveAsync(RedisKeyNames.Identity.UserAll, user.Id);

		return IdentityResult.Success;
	}

	public async Task<IdentityUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
		await LoadAsync(userId);

	public async Task<IdentityUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
	{
		RedisValue userId = await Db.StringGetAsync(RedisKeyNames.Identity.UserByNormalizedUserName(normalizedUserName));
		return userId.IsNullOrEmpty ? null : await LoadAsync(userId!);
	}

	#endregion

	#region IUserEmailStore

	public Task SetEmailAsync(IdentityUser user, string? email, CancellationToken cancellationToken)
	{
		user.Email = email;
		return Task.CompletedTask;
	}

	public Task<string?> GetEmailAsync(IdentityUser user, CancellationToken cancellationToken) =>
		Task.FromResult(user.Email);

	public Task<bool> GetEmailConfirmedAsync(IdentityUser user, CancellationToken cancellationToken) =>
		Task.FromResult(user.EmailConfirmed);

	public Task SetEmailConfirmedAsync(IdentityUser user, bool confirmed, CancellationToken cancellationToken)
	{
		user.EmailConfirmed = confirmed;
		return Task.CompletedTask;
	}

	public async Task<IdentityUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
	{
		RedisValue userId = await Db.StringGetAsync(RedisKeyNames.Identity.UserByNormalizedEmail(normalizedEmail));
		return userId.IsNullOrEmpty ? null : await LoadAsync(userId!);
	}

	public Task<string?> GetNormalizedEmailAsync(IdentityUser user, CancellationToken cancellationToken) =>
		Task.FromResult(user.NormalizedEmail);

	public Task SetNormalizedEmailAsync(IdentityUser user, string? normalizedEmail, CancellationToken cancellationToken)
	{
		user.NormalizedEmail = normalizedEmail;
		return Task.CompletedTask;
	}

	#endregion

	#region IUserPasswordStore

	public Task SetPasswordHashAsync(IdentityUser user, string? passwordHash, CancellationToken cancellationToken)
	{
		user.PasswordHash = passwordHash;
		return Task.CompletedTask;
	}

	public Task<string?> GetPasswordHashAsync(IdentityUser user, CancellationToken cancellationToken) =>
		Task.FromResult(user.PasswordHash);

	public Task<bool> HasPasswordAsync(IdentityUser user, CancellationToken cancellationToken) =>
		Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));

	#endregion

	#region IUserLockoutStore

	public Task<DateTimeOffset?> GetLockoutEndDateAsync(IdentityUser user, CancellationToken cancellationToken) =>
		Task.FromResult(user.LockoutEnd);

	public Task SetLockoutEndDateAsync(
		IdentityUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
	{
		user.LockoutEnd = lockoutEnd;
		return Task.CompletedTask;
	}

	public Task<int> IncrementAccessFailedCountAsync(IdentityUser user, CancellationToken cancellationToken)
	{
		user.AccessFailedCount++;
		return Task.FromResult(user.AccessFailedCount);
	}

	public Task ResetAccessFailedCountAsync(IdentityUser user, CancellationToken cancellationToken)
	{
		user.AccessFailedCount = 0;
		return Task.CompletedTask;
	}

	public Task<int> GetAccessFailedCountAsync(IdentityUser user, CancellationToken cancellationToken) =>
		Task.FromResult(user.AccessFailedCount);

	public Task<bool> GetLockoutEnabledAsync(IdentityUser user, CancellationToken cancellationToken) =>
		Task.FromResult(user.LockoutEnabled);

	public Task SetLockoutEnabledAsync(IdentityUser user, bool enabled, CancellationToken cancellationToken)
	{
		user.LockoutEnabled = enabled;
		return Task.CompletedTask;
	}

	#endregion

	#region IUserRoleStore

	/// <summary>
	/// UserManager는 역할 이름을 정규화(대문자 등)해서 스토어에 넘긴다(EF UserStore와 동일 계약).
	/// UserRoles Set에는 화면(GetRolesAsync)에 그대로 노출되는 표시명(Role.Name)을 저장하고,
	/// RoleUsers Set은 GetUsersInRoleAsync가 정규화된 이름으로 조회하므로 정규화된 이름으로 키를 둔다.
	/// </summary>
	public async Task AddToRoleAsync(IdentityUser user, string normalizedRoleName, CancellationToken cancellationToken)
	{
		string displayName = await ResolveRoleDisplayNameAsync(normalizedRoleName) ?? normalizedRoleName;
		await Db.SetAddAsync(RedisKeyNames.Identity.UserRoles(user.Id), displayName);
		await Db.SetAddAsync(RedisKeyNames.Identity.RoleUsers(normalizedRoleName), user.Id);
	}

	public async Task RemoveFromRoleAsync(
		IdentityUser user, string normalizedRoleName, CancellationToken cancellationToken)
	{
		string displayName = await ResolveRoleDisplayNameAsync(normalizedRoleName) ?? normalizedRoleName;
		await Db.SetRemoveAsync(RedisKeyNames.Identity.UserRoles(user.Id), displayName);
		await Db.SetRemoveAsync(RedisKeyNames.Identity.RoleUsers(normalizedRoleName), user.Id);
	}

	public async Task<IList<string>> GetRolesAsync(IdentityUser user, CancellationToken cancellationToken)
	{
		RedisValue[] roles = await Db.SetMembersAsync(RedisKeyNames.Identity.UserRoles(user.Id));
		return [.. roles.Select(r => (string)r!)];
	}

	public async Task<bool> IsInRoleAsync(
		IdentityUser user, string normalizedRoleName, CancellationToken cancellationToken)
	{
		string displayName = await ResolveRoleDisplayNameAsync(normalizedRoleName) ?? normalizedRoleName;
		return await Db.SetContainsAsync(RedisKeyNames.Identity.UserRoles(user.Id), displayName);
	}

	public async Task<IList<IdentityUser>> GetUsersInRoleAsync(
		string normalizedRoleName, CancellationToken cancellationToken)
	{
		RedisValue[] userIds = await Db.SetMembersAsync(RedisKeyNames.Identity.RoleUsers(normalizedRoleName));
		IdentityUser?[] users = await Task.WhenAll(userIds.Select(id => LoadAsync(id!)));
		return [.. users.Where(u => u is not null).Select(u => u!)];
	}

	private async Task<string?> ResolveRoleDisplayNameAsync(string normalizedRoleName)
	{
		RedisValue roleId = await Db.StringGetAsync(RedisKeyNames.Identity.RoleByNormalizedName(normalizedRoleName));
		if (roleId.IsNullOrEmpty)
		{
			return null;
		}

		RedisValue name = await Db.HashGetAsync(RedisKeyNames.Identity.Role(roleId!), "Name");
		return name.IsNullOrEmpty ? null : (string)name!;
	}

	#endregion

	#region IUserSecurityStampStore

	public Task SetSecurityStampAsync(IdentityUser user, string stamp, CancellationToken cancellationToken)
	{
		user.SecurityStamp = stamp;
		return Task.CompletedTask;
	}

	public Task<string?> GetSecurityStampAsync(IdentityUser user, CancellationToken cancellationToken) =>
		Task.FromResult(user.SecurityStamp);

	#endregion

	/// <summary>
	/// IQueryableUserStore를 구현하지 않으므로(전체 사용자 목록/카운트가 필요한 화면·시딩 로직 전용),
	/// UserAll Set을 통해 전체 사용자를 열거한다.
	/// </summary>
	public async Task<List<IdentityUser>> GetAllUsersAsync(CancellationToken cancellationToken = default)
	{
		RedisValue[] ids = await Db.SetMembersAsync(RedisKeyNames.Identity.UserAll);
		IdentityUser?[] users = await Task.WhenAll(ids.Select(id => LoadAsync(id!)));
		return [.. users.Where(u => u is not null).Select(u => u!).OrderBy(u => u.Id, StringComparer.Ordinal)];
	}

	private async Task<IdentityUser?> LoadAsync(string userId)
	{
		HashEntry[] entries = await Db.HashGetAllAsync(RedisKeyNames.Identity.User(userId));
		if (entries.Length == 0)
		{
			return null;
		}

		Dictionary<string, string> fields = entries.ToDictionary(e => (string)e.Name!, e => (string)e.Value!);
		return new IdentityUser
		{
			Id = userId,
			UserName = fields.GetValueOrDefault("UserName"),
			NormalizedUserName = fields.GetValueOrDefault("NormalizedUserName"),
			Email = fields.GetValueOrDefault("Email"),
			NormalizedEmail = fields.GetValueOrDefault("NormalizedEmail"),
			EmailConfirmed = fields.GetValueOrDefault("EmailConfirmed") == "1",
			PasswordHash = string.IsNullOrEmpty(fields.GetValueOrDefault("PasswordHash"))
				? null : fields["PasswordHash"],
			SecurityStamp = fields.GetValueOrDefault("SecurityStamp"),
			ConcurrencyStamp = fields.GetValueOrDefault("ConcurrencyStamp"),
			LockoutEnd = fields.GetValueOrDefault("LockoutEnd") is { Length: > 0 } lockoutEnd
				? DateTimeOffset.FromUnixTimeSeconds(Int64.Parse(lockoutEnd))
				: null,
			LockoutEnabled = fields.GetValueOrDefault("LockoutEnabled") == "1",
			AccessFailedCount = Int32.Parse(fields.GetValueOrDefault("AccessFailedCount", "0")),
		};
	}

	private static RedisValue[] ToHashPairs(IdentityUser user) =>
	[
		"UserName", user.UserName ?? string.Empty,
		"NormalizedUserName", user.NormalizedUserName ?? string.Empty,
		"Email", user.Email ?? string.Empty,
		"NormalizedEmail", user.NormalizedEmail ?? string.Empty,
		"EmailConfirmed", user.EmailConfirmed ? "1" : "0",
		"PasswordHash", user.PasswordHash ?? string.Empty,
		"SecurityStamp", user.SecurityStamp ?? string.Empty,
		"ConcurrencyStamp", user.ConcurrencyStamp ?? string.Empty,
		"LockoutEnd", user.LockoutEnd?.ToUnixTimeSeconds().ToString() ?? string.Empty,
		"LockoutEnabled", user.LockoutEnabled ? "1" : "0",
		"AccessFailedCount", user.AccessFailedCount.ToString(),
	];

	private static HashEntry[] ToHashEntries(IdentityUser user)
	{
		RedisValue[] pairs = ToHashPairs(user);
		HashEntry[] entries = new HashEntry[pairs.Length / 2];
		for (Int32 i = 0; i < entries.Length; ++i)
		{
			entries[i] = new HashEntry(pairs[i * 2], pairs[(i * 2) + 1]);
		}
		return entries;
	}
}
