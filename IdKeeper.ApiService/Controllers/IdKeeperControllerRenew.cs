using Asp.Versioning;
using IdKeeper.ApiService.AuthorizationFilters;
using IdKeeper.ApiService.Requests;
using IdKeeper.ApiService.Responses;
using IdKeeper.ApiService.Settings;
using IdKeeper.Common.Constants;
using IdKeeper.Database.Redis.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace IdKeeper.ApiService.Controllers;

[ApiController]
[ApiVersion(1)]
[Route("v{version:apiVersion}/IdKeeper")]
[Tags("IdKeeper")]
public class IdKeeperControllerRenew(
	AllocatedIdRepository allocatedIdRepository,
	IdKeeperSetting setting) : ControllerBase
{
	[HttpPost("Renew")]
	[ServiceFilter<XApiKeyFilter>]
	[MapToApiVersion(1)]
	public async Task<ActionResult<IdKeeperResponseV1Renew>> RenewV1Async(
		[FromBody] IdKeeperRequestV1Renew request,
		CancellationToken cancellationToken = default)
	{
		string actor = HttpContext.Items[XApiKeyConstant.XApiKeyOwnerItemKey] as string ?? request.Requester;
		string? remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();

		RenewResult result = await allocatedIdRepository.RenewAsync(
			request.Requester, setting.LeaseDuration, actor, remoteIp, cancellationToken);

		return result switch
		{
			RenewResult.NotFound => Ok(new IdKeeperResponseV1Renew { Ids = [] }),
			RenewResult.Success success => Ok(new IdKeeperResponseV1Renew
			{
				Ids = [.. success.Ids.Select(id =>
					new IdRecord(id, new DateTimeOffset(success.ExpiredAtUtc, TimeSpan.Zero)))]
			}),
			_ => throw new InvalidOperationException($"Unexpected {nameof(RenewResult)}: {result.GetType()}"),
		};
	}
}
