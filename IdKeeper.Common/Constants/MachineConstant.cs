using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace IdKeeper.Common.Constants;

public class MachineConstant
{
	private static Lazy<string> _uniqueProcessId =
		new(() => $"{GetMachineId()}|{Environment.ProcessId}|{GetProcessStartTimeUnixMs()}", isThreadSafe: true);

	/// <summary>
	/// 프로세스 인스턴스마다 유일해야 한다 (기본값은 machineId|PID|프로세스시작시각).
	/// 경고: IdKeeper 서버의 Alloc은 같은 requester가 다시 요청하면 기존에 할당된 노드 ID를
	/// 그대로 돌려주는 멱등 동작을 한다(응답 유실 재시도 복구용). 이 값을 호스트명·서비스명처럼
	/// 여러 프로세스가 공유하는 값으로 바꾸면, "같은 프로세스가 자기 ID를 돌려받는다"가
	/// "서로 다른 두 프로세스가 같은 노드 ID를 받는다"로 바뀌어 Snowflake ID가 중복된다.
	/// </summary>
	public static string UniqueProcessId
	{
		get => _uniqueProcessId.Value;
		set
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(value);

			Lazy<string> newLazy = new(() => value, isThreadSafe: true);

			while (true)
			{
				Lazy<string> current = _uniqueProcessId;
				if (current.IsValueCreated)
				{
					throw new InvalidOperationException("UniqueProcessId는 첫 사용(읽기) 전까지만 설정할 수 있습니다.");
				}

				if (Interlocked.CompareExchange(ref _uniqueProcessId, newLazy, current) == current)
				{
					break;
				}
			}
		}
	}

	public static void Logging(ILogger? logger = default)
	{
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
			logger = loggerFactory.CreateLogger(nameof(MachineConstant));
		}

		logger.LogInformation("UniqueProcessId: {UniqueProcessId}", UniqueProcessId);
	}

	private static string GetMachineId()
	{
		string? machineId = null;

		try
		{
			if (OperatingSystem.IsWindows())
			{
				const string localMachineRoot = "HKEY_LOCAL_MACHINE";
				const string subKey = "SOFTWARE\\Microsoft\\Cryptography";
				const string keyName = $"{localMachineRoot}\\{subKey}";
				const string valueName = "MachineGuid";

				machineId = Normalize((string?)Registry.GetValue(keyName, valueName, string.Empty));
			}
			else if (OperatingSystem.IsLinux())
			{
				if (IsInK8s())
				{
					machineId = ExecuteShellCmd(
						"grep -m1 -oE '/pods/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}'"
						+ " /proc/self/mountinfo"
						+ " | grep -oE '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}'");

					if (string.IsNullOrEmpty(machineId))
					{
						machineId = ExecuteShellCmd(
							"grep -m1 -oE 'pod[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}'"
							+ " /proc/self/cgroup"
							+ " | sed 's/^pod//'");
					}
				}
				else if (IsInDocker())
				{
					machineId = ExecuteShellCmd(
						"cat /proc/self/mountinfo | grep -m1 -oE 'docker/containers/([a-f0-9]+)/' | xargs basename");
				}
				else
				{
					machineId = ReadFirstLine("/etc/machine-id");
				}
			}
			else if (OperatingSystem.IsMacOS())
			{
				machineId = ExecuteShellCmd(
					"/usr/sbin/ioreg -rd1 -c IOPlatformExpertDevice | awk -F\\\" '/IOPlatformUUID/ {print $4}'");
			}
		}
		catch
		{
		}

		if (string.IsNullOrEmpty(machineId))
		{
			machineId = $"{GetBootTimeUtc()}|{Environment.MachineName}";
		}

		const Int32 machineIdMaxLength = 64;
		if (machineId.Length > machineIdMaxLength)
		{
			return machineId[..machineIdMaxLength];
		}

		return machineId;
	}

	private static string? ReadFirstLine(string path)
	{
		try
		{
			if (!File.Exists(path))
			{
				return null;
			}

			using StreamReader sr = new(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
			return Normalize(sr.ReadLine());
		}
		catch
		{
			return null;
		}
	}

	[SupportedOSPlatform("linux")]
	private static bool IsInDocker()
	{
		return File.Exists("/.dockerenv");
	}

	[SupportedOSPlatform("linux")]
	private static bool IsInK8s()
	{
		return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST"));
	}

	[SupportedOSPlatform("linux")]
	[SupportedOSPlatform("macos")]
	private static string? ExecuteShellCmd(string command)
	{
		try
		{
			using Process process = new();
			process.StartInfo.FileName = "/bin/sh";
			process.StartInfo.Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"";
			process.StartInfo.RedirectStandardOutput = true;
			process.StartInfo.RedirectStandardError = true;
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.CreateNoWindow = true;
			process.Start();

			string stdout = process.StandardOutput.ReadToEnd();
			process.WaitForExit(3000);

			return Normalize(stdout);
		}
		catch
		{
			return null;
		}
	}

	private static string? Normalize(string? s)
	{
		if (string.IsNullOrWhiteSpace(s))
		{
			return null;
		}

		return s.Trim();
	}

	private static string GetBootTimeUtc()
	{
		TimeSpan uptimeDuration = TimeSpan.FromMilliseconds(Environment.TickCount64);
		return $"{(DateTimeOffset.UtcNow - uptimeDuration):yyyy-MM-ddTHH:mm:ss.fffzzz}";
	}

	private static string GetProcessStartTimeUnixMs()
	{
		try
		{
			using Process process = Process.GetCurrentProcess();
			return new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeMilliseconds().ToString();
		}
		catch
		{
			return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
		}
	}
}