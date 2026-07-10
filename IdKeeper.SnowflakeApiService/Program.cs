using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using IdKeeper.Common.Constants;
using IdKeeper.Common.Exceptions;
using IdKeeper.Common.Extensions;
using IdKeeper.SnowflakeApiService.Formatters;
using IdKeeper.SnowflakeApiService.HealthChecks;
using IdKeeper.SnowflakeApiService.HostedServices;
using IdKeeper.SnowflakeApiService.HttpClients;
using IdKeeper.SnowflakeApiService.Settings;

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

builder.Services.AddSetting<SnowflakeSetting>(options =>
{
	options.ApplyEnvironmentVariables(builder.Configuration);
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

builder.Services.AddHttpClient<IdKeeperApiClient>(httpClient =>
{
	httpClient.BaseAddress = new("http://apiservice");
});

builder.Services.AddSingleton<SnowflakeHostedService>();
builder.Services.AddHostedService(serviceProvider =>
	serviceProvider.GetRequiredService<SnowflakeHostedService>());

builder.Services.AddHealthChecks()
	.AddCheck<SnowflakeInitHealthCheck>("snowflake-init");

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