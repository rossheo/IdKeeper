using IdKeeper.Database.Redis.Backup;
using IdKeeper.Database.Redis.Models;
using IdKeeper.Database.Redis.Repositories;
using IdKeeper.Web.Settings;
using TickerQ.Utilities.Base;

namespace IdKeeper.Web.Jobs;

/// <summary>
/// 5분마다 가볍게 체크만 하고, 마지막 백업 이후 관리자가 설정한 주기(IntervalMinutes)가
/// 지났을 때만 실제로 백업을 수행한다. TickerQ의 cronExpression은 컴파일 타임 고정값이라
/// 런타임에 바꿀 수 없어(EF 기반 동적 스케줄 저장소는 "Redis 단일 스택" 방향과 어긋나 배제),
/// 자체 판단으로 동적 주기를 흉내낸다. 최소 선택 가능 주기(15분)를 의미 있게 지키려면
/// 체크 주기도 그보다 충분히 촘촘해야 해서 5분으로 잡았다.
/// </summary>
public class RedisBackupJob(
	ILogger<RedisBackupJob> logger,
	RedisBackupService backupService,
	RedisBackupScheduleRepository scheduleRepository,
	RedisBackupDiskGate diskGate,
	RedisBackupSetting setting,
	RedisBackupMetadataStore metadataStore)
{
	public static class FunctionNames
	{
		public const string RedisBackup = "RedisBackup";
	}

	// 체크 주기(5분)의 절반만큼 여유를 둔다 — 단순 elapsed >= IntervalMinutes 비교면
	// 실행이 늦게 끝난 다음 회차부터 체크 시각이 매번 5분씩 밀리는 드리프트가 생긴다.
	private static readonly TimeSpan DueSlack = TimeSpan.FromMinutes(2.5);

	[TickerFunction(functionName: FunctionNames.RedisBackup,
		cronExpression: "0 */5 * * * *")]
	public async Task RedisBackup(
		TickerFunctionContext _, CancellationToken cancellationToken)
	{
		RedisBackupSchedule schedule = await scheduleRepository.GetAsync(cancellationToken);

		if (!IsDue(schedule.IntervalMinutes))
		{
			return;
		}

		try
		{
			Int32 count = await RunBackupAsync(schedule.RetentionCount, cancellationToken);
			logger.LogInformation(
				"{FunctionName} completed. Exported {Count} keys.", FunctionNames.RedisBackup, count);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error during {FunctionName}: {Message}",
				FunctionNames.RedisBackup, ex.Message);
		}
	}

	private bool IsDue(Int32 intervalMinutes)
	{
		IReadOnlyList<FileInfo> existing =
			RedisBackupFileCatalog.ListSortedDescending(setting.RedisBackupDirectory);
		if (existing.Count == 0)
		{
			return true;
		}

		if (!RedisBackupFileCatalog.TryParseTimestampUtc(existing[0].Name, out DateTime lastBackupUtc))
		{
			// 파일명이 손상/변형된 경우 건너뛰는 쪽보다 한 번 더 백업하는 쪽이 안전하다.
			return true;
		}

		TimeSpan elapsed = DateTime.UtcNow - lastBackupUtc;
		TimeSpan threshold = TimeSpan.FromMinutes(intervalMinutes) - DueSlack;
		return elapsed >= threshold;
	}

	/// <summary>
	/// 실제 디스크에 백업 파일을 쓰고 보존 정리까지 수행한다. TickerQ 잡과
	/// "지금 디스크에 백업 생성" 수동 버튼이 이 메서드 하나를 공유하며,
	/// <see cref="RedisBackupDiskGate"/>로 동시 실행을 직렬화한다.
	/// </summary>
	public async Task<Int32> RunBackupAsync(Int32 retentionCount, CancellationToken cancellationToken)
	{
		await diskGate.Semaphore.WaitAsync(cancellationToken);
		try
		{
			Directory.CreateDirectory(setting.RedisBackupDirectory);

			string fileName = RedisBackupFileCatalog.BuildFileName(DateTime.UtcNow);
			string path = Path.Combine(setting.RedisBackupDirectory, fileName);

			Int32 count;
			await using (FileStream stream = File.Create(path))
			{
				count = await backupService.ExportAsync(stream, cancellationToken);
			}

			IReadOnlyList<FileInfo> existing =
				RedisBackupFileCatalog.ListSortedDescending(setting.RedisBackupDirectory);
			HashSet<string> keepFileNames = await metadataStore.GetKeepFileNamesAsync(
				setting.RedisBackupDirectory, existing, cancellationToken);

			RedisBackupFileCatalog.PruneRetained(setting.RedisBackupDirectory, retentionCount, keepFileNames);

			HashSet<string> remaining = [.. RedisBackupFileCatalog
				.ListSortedDescending(setting.RedisBackupDirectory).Select(f => f.Name)];
			foreach (FileInfo removed in existing.Where(f => !remaining.Contains(f.Name)))
			{
				metadataStore.DeleteSidecar(setting.RedisBackupDirectory, removed.Name);
			}

			return count;
		}
		finally
		{
			diskGate.Semaphore.Release();
		}
	}
}
