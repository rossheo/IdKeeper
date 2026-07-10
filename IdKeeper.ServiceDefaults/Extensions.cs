using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class Extensions
{
	private const string HealthEndpointPath = "/health";
	private const string AlivenessEndpointPath = "/alive";

	public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
	{
		builder.ConfigureOpenTelemetry();

		builder.AddDefaultHealthChecks();

		builder.Services.AddServiceDiscovery();

		builder.Services.ConfigureHttpClientDefaults(http =>
		{
			http.AddStandardResilienceHandler();
			http.AddServiceDiscovery();
		});

		return builder;
	}

	public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
	{
		builder.Logging.AddOpenTelemetry(logging =>
		{
			logging.IncludeFormattedMessage = true;
			logging.IncludeScopes = true;
		});

		builder.Services.AddOpenTelemetry()
			.ConfigureResource(resource => resource.AddService(builder.Environment.ApplicationName))
			.WithMetrics(metrics =>
			{
				metrics.AddAspNetCoreInstrumentation()
					.AddHttpClientInstrumentation()
					.AddRuntimeInstrumentation();
			})
			.WithTracing(tracing =>
			{
				tracing.AddSource(builder.Environment.ApplicationName)
					.AddAspNetCoreInstrumentation(options =>
					{
						options.Filter = context =>
							!context.Request.Path.StartsWithSegments(HealthEndpointPath)
							&& !context.Request.Path.StartsWithSegments(AlivenessEndpointPath);
						// NavigationException은 Blazor SSR 내비게이션의 정상 동작이므로
						// ERROR 스팬으로 기록되지 않도록 Activity 상태를 초기화한다.
						options.EnrichWithException = (activity, exception) =>
						{
							if (exception.GetType().FullName ==
								"Microsoft.AspNetCore.Components.NavigationException")
								activity.SetStatus(System.Diagnostics.ActivityStatusCode.Unset);
						};
					})
					.AddHttpClientInstrumentation();
			});

		builder.AddOpenTelemetryExporters();

		return builder;
	}

	private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
	{
		var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

		if (useOtlpExporter)
		{
			builder.Services.AddOpenTelemetry().UseOtlpExporter();
		}

		return builder;
	}

	public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
	{
		builder.Services.AddHealthChecks()
			.AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

		return builder;
	}

	public static WebApplication MapDefaultEndpoints(this WebApplication app)
	{
		// Health check endpoints must be available in all environments for:
		// - Container orchestrator liveness/readiness probes
		// - Aspire WaitFor init containers
		app.MapHealthChecks(HealthEndpointPath);

		app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
		{
			Predicate = static r => r.Tags.Contains("live")
		});

		return app;
	}
}
