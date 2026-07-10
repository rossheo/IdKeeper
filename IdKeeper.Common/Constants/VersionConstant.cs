using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System.Reflection;

namespace IdKeeper.Common.Constants;

public class VersionConstant
{
	public static void Logging(ILogger? logger = default)
	{
		Assembly asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
		string serviceName = asm.GetName().Name ?? "unknown";
		string informational =
			asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
		string fileVersion = asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "unknown";
		string assemblyVersion = asm.GetName().Version?.ToString() ?? "unknown";

		if (logger is null)
		{
			using var loggerFactory = LoggerFactory.Create(
				logBuilder =>
				{
					logBuilder.SetMinimumLevel(LogLevel.Information);
					logBuilder.AddSimpleConsole(o =>
					{
						o.SingleLine = true;
						o.ColorBehavior = LoggerColorBehavior.Enabled;
						o.IncludeScopes = true;
					});
				});
			logger = loggerFactory.CreateLogger(serviceName);
		}

		logger.LogInformation(
			"{Service}. Version: {Informational} (Assembly: {AssemblyVersion}, File: {FileVersion})",
			serviceName, informational, assemblyVersion, fileVersion);
	}
}