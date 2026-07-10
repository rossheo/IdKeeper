using IdKeeper.Common.Constants;
using IdKeeper.SnowflakeApiService.Settings;

namespace IdKeeper.SnowflakeApiService.HttpClients;

public class IdKeeperApiClient
{
	private readonly ILogger _logger;
	private readonly HttpClient _httpClient;
	private readonly SnowflakeSetting _snowflakeSetting;

	public IdKeeperApiClient(
		ILogger<IdKeeperApiClient> logger,
		HttpClient httpClient,
		SnowflakeSetting snowflakeSetting)
	{
		_logger = logger;
		_httpClient = httpClient;
		_snowflakeSetting = snowflakeSetting;

		EnsureApiKeyHeader();
	}

	private void EnsureApiKeyHeader()
	{
		string? apiKey = _snowflakeSetting.IdKeeperApiKey;

		if (string.IsNullOrWhiteSpace(apiKey))
		{
			_logger.LogWarning("IdKeeperApiKey is null or empty." +
				" API requests may fail due to missing authentication header.");
			return;
		}

		_httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
			XApiKeyConstant.XApiKeyHeaderName, apiKey);
	}

	public record IdRecord(Int32 Id, DateTimeOffset ExpiredAtUtc)
	{
		public override string ToString() => $"Id: {Id} ExpiredAtUtc: {ExpiredAtUtc:O}";
	}

	public record RequestV1Alloc(Int32 Count, string Requester);
	public record ResponseV1Alloc(
		DateTimeOffset BaseDateTime,
		ResponseV1Alloc.BitCountRecord BitCount,
		List<IdRecord> Ids)
	{
		public override string ToString()
		{
			string bitCountText = BitCount is null
				? "BitCount={}"
				: $"BitCount={{Timestamp: {BitCount.Timestamp}" +
				$" NodeId: {BitCount.NodeId} SequenceId: {BitCount.SequenceId}}}";

			if (Ids is null || Ids.Count == 0)
			{
				return $"BaseDateTime: {BaseDateTime:O}, {bitCountText}, Count=0, Ids=[]";
			}

			string ids = string.Join(", ", Ids.Select(static r => r.ToString()));
			return $"BaseDateTime: {BaseDateTime:O}, {bitCountText}, Count={Ids.Count}, Ids=[{ids}]";
		}

		public record BitCountRecord(Int32 Timestamp, Int32 NodeId, Int32 SequenceId)
		{
			public override string ToString()
				=> $"Timestamp: {Timestamp} NodeId: {NodeId} SequenceId: {SequenceId}";
		}
	}

	public async Task<ResponseV1Alloc?> PostIdKeeperAlloc(RequestV1Alloc requestAlloc,
		CancellationToken cancellationToken = default)
	{
		try
		{
			string requestUri = "v1/IdKeeper/Alloc";

			HttpResponseMessage response =
				await _httpClient.PostAsJsonAsync(requestUri, requestAlloc, cancellationToken);

			response.EnsureSuccessStatusCode();

			return await response.Content.ReadFromJsonAsync<ResponseV1Alloc>(cancellationToken);
		}
		catch (HttpRequestException ex)
		{
			_logger.LogError(ex,
				"HTTP error from IdKeeper API while allocating IDs. StatusCode={StatusCode}",
				ex.StatusCode);
			return null;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error while allocating node IDs from IdKeeper API.");
			return null;
		}
	}

	public record RequestV1Renew(string Requester);
	public record ResponseV1Renew(List<IdRecord> Ids)
	{
		public override string ToString()
		{
			if (Ids is null || Ids.Count == 0)
			{
				return "Count=0, Ids=[]";
			}

			string ids = string.Join(", ", Ids.Select(static r => r.ToString()));
			return $"Count={Ids.Count}, Ids=[{ids}]";
		}
	}

	public async Task<ResponseV1Renew?> PostIdKeeperRenew(RequestV1Renew requestRenew,
		CancellationToken cancellationToken = default)
	{
		try
		{
			string requestUri = "v1/IdKeeper/Renew";

			HttpResponseMessage response =
				await _httpClient.PostAsJsonAsync(requestUri, requestRenew, cancellationToken);

			response.EnsureSuccessStatusCode();

			return await response.Content.ReadFromJsonAsync<ResponseV1Renew>(cancellationToken);
		}
		catch (HttpRequestException ex)
		{
			_logger.LogError(ex,
				"HTTP error from IdKeeper API while renewing IDs. StatusCode={StatusCode}",
				ex.StatusCode);
			return null;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error while renewing node IDs from IdKeeper API.");
			return null;
		}
	}

	public record RequestV1Remove(string Requester);
	public record ResponseV1Remove(List<Int32> Ids)
	{
		public override string ToString()
		{
			if (Ids is null || Ids.Count == 0)
			{
				return "Count=0, Ids=[]";
			}

			string ids = string.Join(", ", Ids);
			return $"Count={Ids.Count}, Ids=[{ids}]";
		}
	}

	public async Task<ResponseV1Remove?> PostIdKeeperRemove(RequestV1Remove requestRemove,
		CancellationToken cancellationToken = default)
	{
		try
		{
			string requestUri = "v1/IdKeeper/Remove";

			HttpResponseMessage response =
				await _httpClient.PostAsJsonAsync(requestUri, requestRemove, cancellationToken);

			response.EnsureSuccessStatusCode();

			return await response.Content.ReadFromJsonAsync<ResponseV1Remove>(cancellationToken);
		}
		catch (HttpRequestException ex)
		{
			_logger.LogError(ex,
				"HTTP error from IdKeeper API while removing IDs. StatusCode={StatusCode}",
				ex.StatusCode);
			return null;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error while removing node IDs from IdKeeper API.");
			return null;
		}
	}
}