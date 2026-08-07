using IdKeeper.Common.Constants;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace IdKeeper.ApiService.Transformers;

public sealed class XApiKeyDocumentTransformer : IOpenApiDocumentTransformer
{
	private const string _securitySchemeName = "ApiKey";

	public Task TransformAsync(
		OpenApiDocument document,
		OpenApiDocumentTransformerContext context,
		CancellationToken cancellationToken)
	{
		// Components는 null일 수 있다 — 노출되는 DTO가 하나도 없는 문서(예: 원시 타입만 반환하는
		// 엔드포인트로만 구성된 API 버전)에는 생성되지 않는다. 이전에는 !로 경고만 눌러 두어
		// 그런 문서를 요청하면 NullReferenceException으로 500이 났다.
		document.Components ??= new OpenApiComponents();
		document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

		if (!document.Components.SecuritySchemes.TryGetValue(
			_securitySchemeName, out IOpenApiSecurityScheme? scheme))
		{
			scheme = new OpenApiSecurityScheme
			{
				Type = SecuritySchemeType.ApiKey,
				In = ParameterLocation.Header,
				Name = XApiKeyConstant.XApiKeyHeaderName,
				Description = "요청 헤더에 X-API-Key 값을 입력하세요.",
			};

			document.Components.SecuritySchemes[_securitySchemeName] = scheme;
		}

		document.Security ??= [];

		OpenApiSecuritySchemeReference schemeReference = new(_securitySchemeName);
		bool isAdded = document.Security.Any(r => r.ContainsKey(schemeReference));
		if (!isAdded)
		{
			document.Security.Add(new OpenApiSecurityRequirement()
			{
				{
					schemeReference,
					[]
				}
			});
		}

		return Task.CompletedTask;
	}
}