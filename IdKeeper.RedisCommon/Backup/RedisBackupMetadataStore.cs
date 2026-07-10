using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace IdKeeper.Database.Redis.Backup;

/// <summary>
/// 백업 파일(Description/Keep)의 사용자 편집 메타데이터를
/// 파일별 사이드카(*.meta.json)로 저장한다.
/// Redis에 저장하지 않는 이유: "가져오기 전 전체 삭제"는 "IdKeeper/*" 전체를
/// 지우는데, 백업 메타데이터까지 그 프리픽스 아래 두면 백업을 보호하려는
/// 목적 자체가 깨진다. 사이드카 파일명은 "{backupFileName}.meta.json"이라
/// RedisBackupFileCatalog의 "idkeeper-redis-backup-*.ndjson" 글롭에 걸리지
/// 않는다(백업 목록에 유령 항목으로 나타나지 않음).
/// </summary>
public sealed class RedisBackupMetadataStore(ILogger<RedisBackupMetadataStore> logger)
{
	private const string MetadataSuffix = ".meta.json";

	public async Task<RedisBackupFileMetadata> GetAsync(
		string directory, string fileName, CancellationToken cancellationToken = default)
	{
		string path = SidecarPath(directory, fileName);
		if (!File.Exists(path))
		{
			return new RedisBackupFileMetadata();
		}

		try
		{
			await using FileStream stream = File.OpenRead(path);
			RedisBackupFileMetadata? metadata = await JsonSerializer.DeserializeAsync<RedisBackupFileMetadata>(
				stream, cancellationToken: cancellationToken);
			return metadata ?? new RedisBackupFileMetadata();
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "백업 메타데이터를 읽지 못했습니다: {FileName}", fileName);
			return new RedisBackupFileMetadata();
		}
	}

	public async Task SetAsync(
		string directory, string fileName, string description, bool keep,
		CancellationToken cancellationToken = default)
	{
		RedisBackupFileMetadata metadata = new() { Description = description, Keep = keep };
		string path = SidecarPath(directory, fileName);
		string tempPath = $"{path}.tmp";

		await using (FileStream stream = File.Create(tempPath))
		{
			await JsonSerializer.SerializeAsync(stream, metadata, cancellationToken: cancellationToken);
		}
		File.Move(tempPath, path, overwrite: true);
	}

	/// <summary>
	/// "Keep=true"로 표시된 백업 파일명 집합을 돌려준다. 개별 사이드카를 읽지
	/// 못하면 삭제 정책이 실수로 보호 표시된 백업을 지우지 않도록
	/// "읽기 실패 = Keep"으로 안전하게 처리한다.
	/// </summary>
	public async Task<HashSet<string>> GetKeepFileNamesAsync(
		string directory, IReadOnlyList<FileInfo> files, CancellationToken cancellationToken = default)
	{
		HashSet<string> keep = new(StringComparer.Ordinal);
		foreach (FileInfo file in files)
		{
			string path = SidecarPath(directory, file.Name);
			if (!File.Exists(path))
			{
				continue;
			}

			try
			{
				await using FileStream stream = File.OpenRead(path);
				RedisBackupFileMetadata? metadata = await JsonSerializer.DeserializeAsync<RedisBackupFileMetadata>(
					stream, cancellationToken: cancellationToken);
				if (metadata is null || metadata.Keep)
				{
					keep.Add(file.Name);
				}
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex,
					"백업 메타데이터를 읽지 못해 안전하게 삭제 대상에서 제외합니다: {FileName}", file.Name);
				keep.Add(file.Name);
			}
		}
		return keep;
	}

	/// <summary>백업 파일이 삭제될 때 딸린 사이드카도 함께 제거한다.</summary>
	public void DeleteSidecar(string directory, string fileName)
	{
		string path = SidecarPath(directory, fileName);
		if (File.Exists(path))
		{
			File.Delete(path);
		}
	}

	private static string SidecarPath(string directory, string fileName) =>
		Path.Combine(directory, $"{fileName}{MetadataSuffix}");
}
