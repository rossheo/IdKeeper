using IdKeeper.Common.Constants;
using IdKeeper.Database.Redis.Identity;
using IdKeeper.Database.Redis.Repositories;
using Microsoft.AspNetCore.Identity;
using Role = IdKeeper.Database.Redis.Identity.Role;

namespace IdKeeper.Web.HostedServices;

/// <summary>
/// 애플리케이션 시작 시 Identity Role/최초 관리자/FeatureSwitch를 시딩한다.
/// IQueryableUserStore를 구현하지 않으므로 전체 사용자 수/목록은
/// IdentityUserStore.GetAllUsersAsync(UserAll Set 열거)로 확인한다.
/// </summary>
public sealed class RedisSeedHostedService(
	IServiceScopeFactory scopeFactory,
	IdentityUserStore userStore,
	ILogger<RedisSeedHostedService> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using IServiceScope scope = scopeFactory.CreateScope();

		try
		{
			await SeedIdentityRoleAsync(scope, stoppingToken);
			await SeedInitialAdminAsync(scope, stoppingToken);
			await SeedFeatureSwitchAsync(scope, stoppingToken);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "An error occurred while seeding Redis data.");
		}
	}

	private async Task SeedIdentityRoleAsync(IServiceScope scope, CancellationToken stoppingToken)
	{
		RoleManager<IdentityRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

		string[] roleNames = ReflectionConstant.GetPublicStaticStringMemberValues(typeof(Role));
		foreach (string roleName in roleNames)
		{
			if (!await roleManager.RoleExistsAsync(roleName))
			{
				await roleManager.CreateAsync(new IdentityRole(roleName));
			}
		}

		logger.LogInformation("Redis seed identity role completed successfully.");
	}

	private async Task SeedInitialAdminAsync(IServiceScope scope, CancellationToken stoppingToken)
	{
		UserManager<IdentityUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

		List<IdentityUser> users = await userStore.GetAllUsersAsync(stoppingToken);
		if (users.Count != 1)
		{
			return;
		}

		IdentityUser user = users[0];

		IList<string> currentRoles = await userManager.GetRolesAsync(user);
		if (currentRoles.Count != 1 || !currentRoles.Contains(Role.Administrator))
		{
			if (currentRoles.Count > 0)
			{
				await userManager.RemoveFromRolesAsync(user, currentRoles);
			}

			await userManager.AddToRoleAsync(user, Role.Administrator);
		}

		logger.LogInformation(
			"Initial admin configured: {UserName} (Role=Administrator)", user.UserName);
	}

	private async Task SeedFeatureSwitchAsync(IServiceScope scope, CancellationToken stoppingToken)
	{
		FeatureSwitchRepository featureSwitchRepository =
			scope.ServiceProvider.GetRequiredService<FeatureSwitchRepository>();

		string[] keys = ReflectionConstant.GetPublicStaticStringMemberValues(typeof(FeatureSwitchKey));
		foreach (string key in keys)
		{
			if (!await featureSwitchRepository.ExistsAsync(key, stoppingToken))
			{
				// SnowflakeLayoutEdit은 "위험 설정 편집 허용" 게이트라 다른 스위치와 달리
				// 기본 OFF로 시딩한다 — 관리자가 명시적으로 켜야만 레이아웃을 편집할 수 있다.
				bool isEnabled = key != FeatureSwitchKey.SnowflakeLayoutEdit;
				await featureSwitchRepository.CreateAsync(key, isEnabled, description: null, stoppingToken);
			}
		}
	}
}
