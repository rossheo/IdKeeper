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

		// 서비스 식별은 OTel 리소스(service.name)가 담당한다 — 구조화 속성으로 다시 넣으면
		// 수집 서버에 별도 "service" 필드가 생기므로 메시지 템플릿에 포함하지 않는다.
		logger.LogInformation(
			"Version: {Informational} (Assembly: {AssemblyVersion}, File: {FileVersion})",
			informational, assemblyVersion, fileVersion);
	}
}