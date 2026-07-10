using Asp.Versioning;
using IdKeeper.SnowflakeApiService.HostedServices;
using IdKeeper.SnowflakeApiService.Requests;
using IdKeeper.SnowflakeApiService.Responses;
using Microsoft.AspNetCore.Mvc;

namespace IdKeeper.SnowflakeApiService.Controllers;

[ApiController]
[ApiVersion(1)]
[Route("v{version:apiVersion}/SnowflakeId")]
[Tags("SnowflakeId")]
// XApiKeyFilter를 두지 않는다 (의도된 설계): 이 서비스는 외부에 포트를 게시하지 않고
// 내부 네트워크에서만 접근 가능하게 배포되므로, 인증 경계는 애플리케이션 레이어가 아닌
// 네트워크 격리로 처리한다.
public class SnowflakeIdControllerAlloc : ControllerBase
{
	private readonly ILogger _logger;
	private readonly SnowflakeHostedService _snowflakeHostedService;

	public SnowflakeIdControllerAlloc(
		ILogger<SnowflakeIdControllerAlloc> logger,
		SnowflakeHostedService snowflakeHostedService)
	{
		_logger = logger;
		_snowflakeHostedService = snowflakeHostedService;
	}

	[HttpPost("Alloc")]
	[MapToApiVersion(1)]
	public async Task<ActionResult<SnowflakeIdResponseV1Alloc>> AllocV1Async(
		[FromBody] SnowflakeIdRequestV1Alloc request,
		CancellationToken cancellationToken = default)
	{
		if (!await _snowflakeHostedService.IsReadyAsync(cancellationToken))
		{
			return StatusCode(
				StatusCodes.Status503ServiceUnavailable,
				"Snowflake service is not yet initialized.");
		}

		IReadOnlyList<Int64> ids =
			await _snowflakeHostedService.AllocateIdAsync(request.Count, cancellationToken);
		if (ids.Count == 0)
		{
			return StatusCode(
				StatusCodes.Status503ServiceUnavailable,
				"Snowflake service is unavailable.");
		}

		return Ok(new SnowflakeIdResponseV1Alloc { Ids = ids });
	}
}