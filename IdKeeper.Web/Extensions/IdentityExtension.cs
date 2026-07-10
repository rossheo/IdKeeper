using Microsoft.AspNetCore.Identity;

namespace IdKeeper.Web.Extensions;

public static class IdentityExtension
{
	public static async Task<IdentityResult> AssignRoleAsync(
		this UserManager<IdentityUser> userManager,
		IdentityUser user,
		string role)
	{
		HashSet<string> roles = [.. await userManager.GetRolesAsync(user)];
		if (!roles.Contains(role))
		{
			var result = await userManager.AddToRoleAsync(user, role);
			if (!result.Succeeded)
			{
				return result;
			}
		}

		return await userManager.RemoveFromRolesAsync(user, [.. roles.Except([role])]);
	}
}
