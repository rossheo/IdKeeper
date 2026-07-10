using IdKeeper.ApiService.Caching;
using IdKeeper.Common.Constants;
using IdKeeper.Database.Redis.Models;
using IdKeeper.Database.Redis.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;
using System.Net;

namespace IdKeeper.ApiService.AuthorizationFilters;

public class XApiKeyFilter : IAsyncAuthorizationFilter
{
	private static readonly IPNetwork[] LoopbackNetworks =
	[
		IPNetwork.Parse("127.0.0.0/8"),
		IPNetwork.Parse("::1/128"),
	];

	private readonly ILogger<XApiKeyFilter> _logger;
	private readonly XApiKeyRepository _xApiKeyRepository;
	private readonly CidrCache _cidrCache;

	public XApiKeyFilter(
		ILogger<XApiKeyFilter> logger,
		XApiKeyRepository xApiKeyRepository,
		CidrCache cidrCache)
	{
		_logger = logger;
		_xApiKeyRepository = xApiKeyRepository;
		_cidrCache = cidrCache;
	}

	public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
	{
		if (!IsIPAllowed(context))
		{
			return;
		}

		await IsXApiKeyAuthorizedAsync(context);
	}

	private async Task<bool> IsXApiKeyAuthorizedAsync(AuthorizationFilterContext context)
	{
		if (!context.HttpContext.Request.Headers.TryGetValue(
			XApiKeyConstant.XApiKeyHeaderName, out StringValues requestXApiKeys))
		{
			_logger.LogWarning("XApiKey is missing.");
			context.Result = new UnauthorizedObjectResult("XApiKey is missing.");
			return false;
		}

		string requestXApiKey = requestXApiKeys.FirstOrDefault() ?? string.Empty;
		if (!requestXApiKey.StartsWith(XApiKeyConstant.XApiKeyPrefix))
		{
			_logger.LogWarning("Request XApiKey is not valid.");
			context.Result = new UnauthorizedObjectResult("Request XApiKey is not valid.");
			return false;
		}

		XApiKey? xApiKey = await _xApiKeyRepository.FindByApiKeyAsync(
			requestXApiKey, context.HttpContext.RequestAborted);

		if (xApiKey is null)
		{
			_logger.LogWarning("Invalid XApiKey.");
			context.Result = new UnauthorizedObjectResult("Invalid XApiKey.");
			return false;
		}

		if (xApiKey.ExpiredAtUtc is not null && xApiKey.ExpiredAtUtc < DateTime.UtcNow)
		{
			_logger.LogWarning("Expired XApiKey.");
			context.Result = new UnauthorizedObjectResult("Expired XApiKey.");
			return false;
		}

		context.HttpContext.Items[XApiKeyConstant.XApiKeyOwnerItemKey] = xApiKey.Owner;
		return true;
	}

	private bool IsIPAllowed(AuthorizationFilterContext context)
	{
		if (!TryGetClientIp(context, out IPAddress? clientIp))
		{
			_logger.LogWarning("Client IP not found.");
			context.Result = new ObjectResult("Client IP not found.") { StatusCode = StatusCodes.Status403Forbidden };
			return false;
		}

		if (!_cidrCache.IsLoaded)
		{
			_logger.LogWarning("CIDR cache is not yet loaded.");
			context.Result = new ObjectResult("Service temporarily unavailable.")
				{ StatusCode = StatusCodes.Status503ServiceUnavailable };
			return false;
		}

		IPNetwork[] allowedNetworks = _cidrCache.Networks;

		// 허용 CIDR이 하나도 등록되지 않은 경우 IP 제한 없이 전체 허용한다 (의도된 정책).
		if (allowedNetworks.Length == 0)
		{
			return true;
		}

		foreach (IPNetwork loopback in LoopbackNetworks)
		{
			if (loopback.Contains(clientIp))
				return true;
		}

		foreach (IPNetwork network in allowedNetworks)
		{
			if (network.Contains(clientIp))
				return true;
		}

		_logger.LogWarning("Forbidden IP address. {ClientIp}", clientIp);
		context.Result = new ObjectResult("Forbidden IP address.") { StatusCode = StatusCodes.Status403Forbidden };
		return false;
	}

	private static bool TryGetClientIp(AuthorizationFilterContext context, out IPAddress ip)
	{
		ip = default!;

		// ForwardedHeadersMiddleware가 신뢰된 프록시의 X-Forwarded-For를 RemoteIpAddress에 반영합니다.
		// 이 메서드는 헤더를 직접 파싱하지 않습니다 (스푸핑 방지).
		IPAddress? remote = context.HttpContext.Connection.RemoteIpAddress;
		if (remote is not null)
		{
			ip = remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote;
			return true;
		}

		return false;
	}
}
