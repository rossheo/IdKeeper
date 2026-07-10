using IdKeeper.Database.Redis.Backup;
using IdKeeper.Database.Redis.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

if (args.Length == 0)
{
	PrintUsage();
	return 1;
}

string command = args[0];
Dictionary<string, string> options = ParseOptions(args.Skip(1));

HostApplicationBuilder builder = Host.CreateApplicationBuilder();
builder.AddIdKeeperRedis();

using IHost host = builder.Build();
RedisBackupService backupService = host.Services.GetRequiredService<RedisBackupService>();

return command switch
{
	"export" => await RunExportAsync(backupService, options),
	"import" => await RunImportAsync(backupService, options),
	_ => PrintUsageAndFail(),
};

static async Task<Int32> RunExportAsync(RedisBackupService service, Dictionary<string, string> options)
{
	if (!options.TryGetValue("output", out string? path))
	{
		Console.Error.WriteLine("--output <file> is required.");
		return 1;
	}

	await using FileStream stream = File.Create(path);
	Int32 count = await service.ExportAsync(stream);
	Console.WriteLine($"Exported {count} keys to {path}");
	return 0;
}

static async Task<Int32> RunImportAsync(RedisBackupService service, Dictionary<string, string> options)
{
	if (!options.TryGetValue("input", out string? path))
	{
		Console.Error.WriteLine("--input <file> is required.");
		return 1;
	}

	bool purge = options.ContainsKey("purge");
	await using FileStream stream = File.OpenRead(path);
	Int32 count = await service.ImportAsync(stream, purge);
	Console.WriteLine($"Imported {count} keys from {path}{(purge ? " (purged before import)" : string.Empty)}");
	return 0;
}

static Dictionary<string, string> ParseOptions(IEnumerable<string> args)
{
	Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
	string[] arr = [.. args];
	for (Int32 i = 0; i < arr.Length; ++i)
	{
		if (!arr[i].StartsWith("--", StringComparison.Ordinal))
		{
			continue;
		}

		string key = arr[i][2..];
		if (i + 1 < arr.Length && !arr[i + 1].StartsWith("--", StringComparison.Ordinal))
		{
			result[key] = arr[++i];
		}
		else
		{
			result[key] = "true";
		}
	}
	return result;
}

static Int32 PrintUsageAndFail()
{
	PrintUsage();
	return 1;
}

static void PrintUsage()
{
	Console.WriteLine("""
		IdKeeper.RedisBackupTool

		Redis 연결 문자열은 ConnectionStrings__redis 환경변수로 전달한다
		(형식: host:port,password=xxx — Aspire StackExchange.Redis 클라이언트 규칙).

		Usage:
		  export --output <file>
		  import --input <file> [--purge]
		""");
}
