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
		document.Components!.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

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