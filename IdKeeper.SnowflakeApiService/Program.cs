using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using IdKeeper.Client;
using IdKeeper.Common.Constants;
using IdKeeper.Common.Exceptions;
using IdKeeper.SnowflakeApiService.Formatters;

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

// 임대 생명주기 전체를 IdKeeper.Client가 담당한다. 이 서비스는 그 위의 HTTP 게이트웨이다.
// 설정은 "SnowflakeSetting" 섹션에서 읽고, 배포 환경의 환경변수가 그 위를 덮는다.
builder.Services.AddIdKeeperSnowflake(options =>
{
	builder.Configuration.GetSection("SnowflakeSetting").Bind(options);

	string? baseAddress = builder.Configuration["IDKEEPER_BASEADDRESS"]
		?? builder.Configuration["services:apiservice:http:0"];
	if (!string.IsNullOrWhiteSpace(baseAddress))
	{
		options.BaseAddress = new Uri(baseAddress);
	}

	string? apiKey = builder.Configuration["IDKEEPER_APIKEY"];
	if (!string.IsNullOrWhiteSpace(apiKey))
	{
		options.ApiKey = apiKey;
	}

	string? generatorCount = builder.Configuration["IDKEEPER_GENERATOR_COUNT"];
	if (!string.IsNullOrWhiteSpace(generatorCount)
		&& Int32.TryParse(generatorCount, out Int32 parsedCount))
	{
		options.GeneratorCount = parsedCount;
	}
});

builder.Services.AddControllers(options =>
{
	options.OutputFormatters.Add(new ProtobufBlockedIntegerOutputFormatter());
});

builder.Services.AddOpenApi();

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

// 헬스체크 이름을 유지한다 — Aspire AppHost가 /health로 기동 순서를 게이팅한다.
builder.Services.AddHealthChecks()
	.AddIdKeeperSnowflake("snowflake-init");

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
// 실제 requester와 로그가 어긋나지 않도록 라이브러리가 산출한 식별자를 남긴다.
app.Logger.LogInformation("Requester: {Requester}", SnowflakeClientIdentity.Current);

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