using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using FluentValidation;
using FluentValidation.AspNetCore;
using IdKeeper.ApiService.AuthorizationFilters;
using IdKeeper.ApiService.Caching;
using IdKeeper.ApiService.Settings;
using IdKeeper.ApiService.Transformers;
using IdKeeper.Common.Constants;
using IdKeeper.Common.Exceptions;
using IdKeeper.Common.Extensions;
using IdKeeper.Database.Redis.Extensions;
using Microsoft.AspNetCore.HttpOverrides;
using TickerQ.DependencyInjection;

SnowflakeConstant.EnsureValid();

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddSeqEndpoint(connectionName: "seq");

builder.Services.AddProblemDetails(configure =>
{
	configure.CustomizeProblemDetails = context =>
	{
		context.ProblemDetails.Extensions.TryAdd("requestId", context.HttpContext.TraceIdentifier);
	};
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddSetting<IdKeeperSetting>();

builder.AddIdKeeperRedis();

// X-Forwarded-For 처리: 신뢰된 프록시(RFC 1918 사설 네트워크)의 헤더만 적용합니다.
// XApiKeyFilter는 이 미들웨어가 설정한 RemoteIpAddress만 읽습니다 (직접 파싱 금지).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
	options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
	options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.0.0.0/8"));
	options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12"));
	options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("192.168.0.0/16"));
});

builder.Services.AddControllers();

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.AddScoped<XApiKeyFilter>();

builder.Services.AddSingleton<CidrCache>();
builder.Services.AddHostedService<CidrCacheRefreshService>();

builder.Services.AddTickerQ();

builder.Services.AddOpenApi(options =>
{
	// SwaggerUI 에서 X-API-Key 헤더 입력
	options.AddDocumentTransformer<XApiKeyDocumentTransformer>();
});

builder.Services.AddApiVersioning(options =>
{
	options.DefaultApiVersion = new(1);
	options.AssumeDefaultVersionWhenUnspecified = true;
	options.ReportApiVersions = true;
	options.ApiVersionReader = new UrlSegmentApiVersionReader();
})
.AddMvc()
.AddApiExplorer(options =>
{
	options.GroupNameFormat = "'v'VVV";
	options.SubstituteApiVersionInUrl = true;
});

if (builder.Environment.IsDevelopment())
{
	builder.Services.AddCors(options =>
	{
		options.AddDefaultPolicy(
			policy =>
			{
				policy
				.AllowAnyOrigin()
				.AllowAnyHeader()
				.AllowAnyMethod();
			});
	});
}

WebApplication app = builder.Build();
VersionConstant.Logging(app.Logger);
MachineConstant.Logging(app.Logger);

app.UseForwardedHeaders();
app.UseTickerQ();

if (app.Environment.IsDevelopment())
{
	app.UseDeveloperExceptionPage();

	app.UseCors();

	app.MapOpenApi();

	app.UseSwaggerUI(options =>
	{
		IApiVersionDescriptionProvider provider =
			app.Services.GetRequiredService<IApiVersionDescriptionProvider>();

		foreach (ApiVersionDescription description in provider.ApiVersionDescriptions)
		{
			string group = description.GroupName;
			options.SwaggerEndpoint($"/openapi/{group}.json", group.ToUpperInvariant());
		}
	});
}
else
{
	app.UseExceptionHandler();
}

app.MapDefaultEndpoints();
app.MapControllers();

app.Run();