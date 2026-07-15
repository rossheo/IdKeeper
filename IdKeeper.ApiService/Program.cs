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
using IdKeeper.Database.Redis.Repositories;
using Microsoft.AspNetCore.HttpOverrides;
using TickerQ.DependencyInjection;

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

builder.Services.AddSingleton<SnowflakeLayoutHolder>();

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

// SnowflakeLayoutHolder는 Redis 접근이 필요해 SnowflakeConstant.EnsureValid()의 정적 호출을
// 여기(app.Build() 이후, DI 준비 시점)로 대체한다. 값은 프로세스 생명주기 동안 고정 —
// 변경 후 반영하려면 재시작이 필요하다(/snowflakelayout 페이지 경고 참고).
await using (AsyncServiceScope snowflakeLayoutScope = app.Services.CreateAsyncScope())
{
	SnowflakeLayoutRepository snowflakeLayoutRepository =
		snowflakeLayoutScope.ServiceProvider.GetRequiredService<SnowflakeLayoutRepository>();
	SnowflakeLayout snowflakeLayout = await snowflakeLayoutRepository.GetAsync();
	snowflakeLayout.EnsureValid();
	snowflakeLayoutScope.ServiceProvider.GetRequiredService<SnowflakeLayoutHolder>().Initialize(snowflakeLayout);
}

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