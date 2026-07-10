using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace IdKeeper.SnowflakeApiService.Controllers;

[ApiController]
[ApiVersion(1)]
[Route("v{version:apiVersion}/SnowflakeId")]
[Tags("SnowflakeId")]
public class SnowflakeIdControllerVersion : ControllerBase
{
	private static readonly Assembly s_asm = typeof(SnowflakeIdControllerVersion).Assembly;
	private static readonly string s_product = s_asm.GetName().Name ?? "unknown";
	private static readonly string s_informationalVersion =
		s_asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

	// 별도 Response DTO 없이 익명 객체를 반환한다 (버전 정보 노출용 엔드포인트로,
	// 계약 안정성보다 편의성을 우선한 의도된 설계).
	[HttpGet("Version")]
	[MapToApiVersion(1)]
	public ActionResult<object> GetVersionV1()
	{
		return Ok(new
		{
			Product = s_product,
			InformationalVersion = s_informationalVersion,
		});
	}
}