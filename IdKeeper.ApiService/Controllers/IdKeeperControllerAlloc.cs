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
public class IdKeeperControllerAlloc(
	ILogger<IdKeeperControllerAlloc> logger,
	AllocatedIdRepository allocatedIdRepository,
	IdKeeperSetting setting,
	SnowflakeLayoutHolder snowflakeLayoutHolder,
	ClockSkewPolicy clockSkewPolicy) : ControllerBase
{
	[HttpPost("Alloc")]
	[ServiceFilter<XApiKeyFilter>]
	[MapToApiVersion(1)]
	public async Task<ActionResult<IdKeeperResponseV1Alloc>> AllocV1Async(
		[FromBody] IdKeeperRequestV1Alloc request,
		CancellationToken cancellationToken = default)
	{
		SnowflakeLayout layout = snowflakeLayoutHolder.Current;
		Int32 maxNodeIdInclusive = layout.MaxNodeIdInclusive;

		string actor = HttpContext.Items[XApiKeyConstant.XApiKeyOwnerItemKey] as string ?? request.Requester;
		string? remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();

		// 반드시 AllocAsync 이전에 판정한다. 뒤에 두면 리스를 이미 쓴 뒤 거부하게 되고,
		// Alloc이 멱등이라 재시도가 같은 ID를 받아 또 거부되어 리스가 고아로 남는다.
		ClockSkewVerdict skewVerdict = await clockSkewPolicy.EvaluateAsync(
			"Alloc", request.Requester, request.ClientUtcNow, remoteIp,
			allowReject: true, cancellationToken);
		if (skewVerdict.Reject)
		{
			return Conflict(skewVerdict.RejectMessage);
		}

		AllocResult result = await allocatedIdRepository.AllocAsync(
			request.Requester, request.Count, maxNodeIdInclusive, setting.FirstTimeExpiration,
			actor, remoteIp, description: null, cancellationToken);

		switch (result)
		{
			case AllocResult.AlreadyExists:
				logger.LogWarning("Already exist same requester. Cannot allocate ids.");
				return Conflict("Already exist same requester. Cannot allocate ids.");

			case AllocResult.InsufficientIds:
				logger.LogWarning(
					"Concurrent allocation contention or capacity exceeded. " +
					"Requester {Requester}, Requested {RequestedCount}.",
					request.Requester, request.Count);
				return Conflict(
					$"Requested count {request.Count} exceeds the remaining available IDs, or concurrent " +
					"allocation reduced availability. Please try again.");

			case AllocResult.Success success:
				IdKeeperResponseV1Alloc response = new()
				{
					BaseDateTime = new DateTimeOffset(layout.BaseDateTime, TimeSpan.Zero),
					BitCount = new IdKeeperResponseV1Alloc.BitCountRecord(
						layout.BitCountOfTimestamp,
						layout.BitCountOfNodeId,
						layout.BitCountOfSequenceId),
					Ids = [.. success.Ids.Select(id =>
						new IdRecord(id, new DateTimeOffset(success.ExpiredAtUtc, TimeSpan.Zero)))]
				};
				return Ok(response);

			default:
				throw new InvalidOperationException($"Unexpected {nameof(AllocResult)}: {result.GetType()}");
		}
	}
}
