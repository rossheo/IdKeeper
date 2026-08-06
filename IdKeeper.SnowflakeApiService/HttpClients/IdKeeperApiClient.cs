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

	/// <summary>
	/// 공통 POST 경로. 실패 시 null을 반환해 호출자가 재시도를 판단하게 하되,
	/// 호출자가 취소한 경우(셧다운 등)는 실패가 아니므로 그대로 전파한다.
	/// </summary>
	private async Task<TResponse?> PostAsync<TRequest, TResponse>(
		string requestUri, TRequest request, string operation,
		CancellationToken cancellationToken)
		where TResponse : class
	{
		try
		{
			HttpResponseMessage response =
				await _httpClient.PostAsJsonAsync(requestUri, request, cancellationToken);

			response.EnsureSuccessStatusCode();

			return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// 호출자 토큰에 의한 취소는 오류가 아니다. 호출자(SnowflakeHostedService)가
			// OperationCanceledException을 이미 처리하므로 로깅 없이 전파한다.
			throw;
		}
		catch (TaskCanceledException ex)
		{
			// 호출자 토큰이 취소되지 않은 TaskCanceledException = HttpClient 타임아웃.
			// 일시적 실패이므로 null을 반환해 호출자가 재시도하게 한다.
			_logger.LogError(ex, "Timeout from IdKeeper API while {Operation}.", operation);
			return null;
		}
		catch (HttpRequestException ex)
		{
			_logger.LogError(ex,
				"HTTP error from IdKeeper API while {Operation}. StatusCode={StatusCode}",
				operation,
				ex.StatusCode);
			return null;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Unexpected error from IdKeeper API while {Operation}.", operation);
			return null;
		}
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

	public Task<ResponseV1Alloc?> PostIdKeeperAlloc(RequestV1Alloc requestAlloc,
		CancellationToken cancellationToken = default)
		=> PostAsync<RequestV1Alloc, ResponseV1Alloc>(
			"v1/IdKeeper/Alloc", requestAlloc, "allocating node ids", cancellationToken);

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

	// 반환값 계약: null = 전송 실패(재시도 가능), Ids가 빈 목록 = 서버에 리스가 없음(확정).
	// SnowflakeHostedService.RenewAsync가 이 둘을 다르게 처리한다.
	public Task<ResponseV1Renew?> PostIdKeeperRenew(RequestV1Renew requestRenew,
		CancellationToken cancellationToken = default)
		=> PostAsync<RequestV1Renew, ResponseV1Renew>(
			"v1/IdKeeper/Renew", requestRenew, "renewing node ids", cancellationToken);

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

	public Task<ResponseV1Remove?> PostIdKeeperRemove(RequestV1Remove requestRemove,
		CancellationToken cancellationToken = default)
		=> PostAsync<RequestV1Remove, ResponseV1Remove>(
			"v1/IdKeeper/Remove", requestRemove, "removing node ids", cancellationToken);
}