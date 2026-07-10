using Asp.Versioning;
using IdKeeper.ApiService.AuthorizationFilters;
using IdKeeper.ApiService.Requests;
using IdKeeper.ApiService.Responses;
using IdKeeper.Common.Constants;
using IdKeeper.Database.Redis.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace IdKeeper.ApiService.Controllers;

[ApiController]
[ApiVersion(1)]
[Route("v{version:apiVersion}/IdKeeper")]
[Tags("IdKeeper")]
public class IdKeeperControllerRemove(AllocatedIdRepository allocatedIdRepository) : ControllerBase
{
	[HttpPost("Remove")]
	[ServiceFilter<XApiKeyFilter>]
	[MapToApiVersion(1)]
	public async Task<ActionResult<IdKeeperResponseV1Remove>> RemoveV1Async(
		[FromBody] IdKeeperRequestV1Remove request,
		CancellationToken cancellationToken = default)
	{
		string actor = HttpContext.Items[XApiKeyConstant.XApiKeyOwnerItemKey] as string ?? request.Requester;
		string? remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();

		List<Int32> removedIds = await allocatedIdRepository.RemoveAsync(
			request.Requester, actor, remoteIp, cancellationToken);

		return Ok(new IdKeeperResponseV1Remove { Ids = removedIds });
	}
}
