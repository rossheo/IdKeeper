using Asp.Versioning;
using IdKeeper.ApiService.AuthorizationFilters;
using IdKeeper.ApiService.Responses;
using IdKeeper.Common.Constants;
using IdKeeper.Database.Redis.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace IdKeeper.ApiService.Controllers;

[ApiController]
[ApiVersion(1)]
[Route("v{version:apiVersion}/IdKeeper")]
[Tags("IdKeeper")]
public class IdKeeperControllerCountOfRemainId(AllocatedIdRepository allocatedIdRepository) : ControllerBase
{
	[HttpGet("CountOfRemainId")]
	[ServiceFilter<XApiKeyFilter>]
	[MapToApiVersion(1)]
	public async Task<ActionResult<IdKeeperResponseV1CountOfRemainId>> CountOfRemainIdV1Async(
		CancellationToken cancellationToken = default)
	{
		const Int64 totalIds = (Int64)SnowflakeConstant.MaxNodeIdInclusive + 1;

		Int64 allocatedCount = await allocatedIdRepository.CountOfAllocatedAsync(cancellationToken);
		Int64 remainNodeIdCount = Math.Max(0L, totalIds - allocatedCount);

		return Ok(new IdKeeperResponseV1CountOfRemainId
		{
			CountOfMaxNodeId = (Int32)totalIds,
			CountOfRemainId = (Int32)remainNodeIdCount
		});
	}
}
