using IdKeeper.Database.Redis.Backup;
using IdKeeper.Database.Redis.Identity;
using IdKeeper.Database.Redis.Locking;
using IdKeeper.Database.Redis.Repositories;
using IdKeeper.Database.Redis.Scripts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IdKeeper.Database.Redis.Extensions;

public static class RedisExtensions
{
	public static IHostApplicationBuilder AddIdKeeperRedis(
		this IHostApplicationBuilder builder, string connectionName = "redis")
	{
		builder.AddRedisClient(connectionName);

		builder.Services.AddSingleton<LuaScriptLoader>();
		builder.Services.AddSingleton<RedisLockFactory>();

		builder.Services.AddSingleton<AllocatedIdRepository>();
		builder.Services.AddSingleton<XApiKeyRepository>();
		builder.Services.AddSingleton<XApiAllowedCidrRepository>();
		builder.Services.AddSingleton<XApiAllowedHostnameRepository>();
		builder.Services.AddSingleton<FeatureSwitchRepository>();
		builder.Services.AddSingleton<AuditLogRepository>();
		builder.Services.AddSingleton<RedisBackupScheduleRepository>();
		builder.Services.AddSingleton<SnowflakeLayoutRepository>();

		builder.Services.AddSingleton<IdentityUserStore>();
		builder.Services.AddSingleton<IdentityRoleStore>();

		builder.Services.AddSingleton<RedisBackupService>();
		builder.Services.AddSingleton<RedisBackupMetadataStore>();

		return builder;
	}
}
