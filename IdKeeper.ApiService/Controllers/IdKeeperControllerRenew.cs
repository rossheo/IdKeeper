using Asp.Versioning;
using IdKeeper.ApiService.AuthorizationFilters;
using IdKeeper.ApiService.ClockSkew;
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
	IdKeeperSetting setting,
	ClockSkewPolicy clockSkewPolicy) : ControllerBase
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

		// 의도된 설계: Renew는 시계 오차로 거부하지 않고 관측·경고만 한다. 갱신이 성공하는
		// 동안에는 만료 시각이 계속 밀려 서버의 회수 시점에 도달하지 않으므로, 시계가 틀어져
		// 있어도 그 자체로는 ID가 중복되지 않는다. 여기서 거부하면 지금 안전한 프로세스를
		// 죽이게 되고, NTP 장애는 보통 클러스터 단위라 전면 장애로 번진다.
		await clockSkewPolicy.EvaluateAsync(
			"Renew", request.Requester, request.ClientUtcNow, remoteIp,
			allowReject: false, cancellationToken);

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
