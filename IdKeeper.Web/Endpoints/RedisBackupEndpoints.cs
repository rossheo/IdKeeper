using IdKeeper.Common.Constants;
using IdKeeper.Database.Redis.Backup;
using IdKeeper.Database.Redis.Identity;
using IdKeeper.Database.Redis.Repositories;
using IdKeeper.Web.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IdKeeper.Web.Endpoints;

public static class RedisBackupEndpoints
{
	public static IEndpointRouteBuilder MapRedisBackupEndpoints(this IEndpointRouteBuilder endpoints)
	{
		endpoints.MapGet("/redisbackup/export", async (
			HttpContext context,
			[FromServices] RedisBackupService backupService,
			[FromServices] AuditLogRepository auditLogRepository) =>
		{
			string actor = context.User.Identity?.Name ?? "unknown";

			using MemoryStream stream = new();
			Int32 count = await backupService.ExportAsync(stream);

			await auditLogRepository.AppendAsync(AuditLogAction.RedisBackupExported, actor, detail: $"{count} keys");

			string fileName = $"idkeeper-redis-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.ndjson";
			return TypedResults.File(stream.ToArray(), "application/x-ndjson", fileName);
		}).RequireAuthorization(new AuthorizeAttribute { Roles = Role.Administrator });

		endpoints.MapGet("/redisbackup/download", async (
			HttpContext context,
			[FromQuery] string file,
			[FromServices] RedisBackupSetting setting,
			[FromServices] AuditLogRepository auditLogRepository) =>
		{
			string fileName = Path.GetFileName(file);
			if (!fileName.StartsWith(RedisBackupFileCatalog.FilePrefix, StringComparison.Ordinal) ||
				!fileName.EndsWith(RedisBackupFileCatalog.FileExtension, StringComparison.Ordinal))
			{
				return Results.BadRequest("잘못된 파일명입니다.");
			}

			string path = Path.Combine(setting.RedisBackupDirectory, fileName);
			if (!File.Exists(path))
			{
				return Results.NotFound();
			}

			string actor = context.User.Identity?.Name ?? "unknown";
			await auditLogRepository.AppendAsync(
				AuditLogAction.RedisBackupExported, actor, detail: $"scheduled-download:{fileName}");

			return Results.File(path, "application/x-ndjson", fileName);
		}).RequireAuthorization(new AuthorizeAttribute { Roles = Role.Administrator });

		return endpoints;
	}
}
