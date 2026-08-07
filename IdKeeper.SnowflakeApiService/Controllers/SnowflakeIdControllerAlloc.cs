using Asp.Versioning;
using IdKeeper.Client;
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
	private readonly ISnowflakeIdGenerator _idGenerator;

	public SnowflakeIdControllerAlloc(
		ILogger<SnowflakeIdControllerAlloc> logger,
		ISnowflakeIdGenerator idGenerator)
	{
		_logger = logger;
		_idGenerator = idGenerator;
	}

	[HttpPost("Alloc")]
	[MapToApiVersion(1)]
	public async Task<ActionResult<SnowflakeIdResponseV1Alloc>> AllocV1Async(
		[FromBody] SnowflakeIdRequestV1Alloc request,
		CancellationToken cancellationToken = default)
	{
		try
		{
			IReadOnlyList<Int64> ids =
				await _idGenerator.NextIdsAsync(request.Count, cancellationToken);
			return Ok(new SnowflakeIdResponseV1Alloc { Ids = ids });
		}
		catch (SnowflakeNotReadyException ex)
		{
			// 아직 임대를 받지 못했거나(기동 직후) 만료되어 발급이 차단된 상태.
			// 라이브러리는 예외로 알리지만 HTTP로는 503이 적절하다.
			_logger.LogWarning(ex, "Snowflake id generator is not ready.");
			return StatusCode(
				StatusCodes.Status503ServiceUnavailable,
				"Snowflake service is unavailable.");
		}
	}
}