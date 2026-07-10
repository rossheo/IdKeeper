using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Diagnostics;

namespace IdKeeper.AppHost.Extensions;

internal static class ResourceBuilderExtension
{
	internal static IResourceBuilder<T> WithSwaggerUI<T>(this IResourceBuilder<T> builder)
		where T : IResourceWithEndpoints
	{
		return builder.WithOpenApiDocs(
			name: "swaggerUIDocs",
			displayName: "Swagger API Documentation",
			openApiUiPath: "swagger");
	}

	private static IResourceBuilder<T> WithOpenApiDocs<T>(this IResourceBuilder<T> builder,
		string name,
		string displayName,
		string openApiUiPath)
		where T : IResourceWithEndpoints
	{
		return builder.WithCommand(
			name,
			displayName,
			executeCommand: async _ =>
			{
				try
				{
					EndpointReference endpoint = builder.GetEndpoint("https");

					string url = $"{endpoint.Url}/{openApiUiPath}";

					Process.Start(new ProcessStartInfo
					{
						FileName = url,
						UseShellExecute = true
					});

					return await Task.FromResult(new ExecuteCommandResult { Success = true });
				}
				catch (Exception ex)
				{
					return new ExecuteCommandResult { Success = false, Message = ex.ToString() };
				}
			},
			commandOptions: new CommandOptions
			{
				UpdateState = context => context.ResourceSnapshot.HealthStatus == HealthStatus.Healthy ?
					ResourceCommandState.Enabled : ResourceCommandState.Disabled,
				IconName = "Document",
				IconVariant = IconVariant.Filled
			});
	}
}