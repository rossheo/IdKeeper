using System.Diagnostics;
using System.Text.RegularExpressions;

namespace IdKeeper.Client;

/// <summary>
/// 이 프로세스를 IdKeeper 서버에 식별시키는 값(requester)을 산출한다.
/// 형식은 <c>{machineId}|{PID}|{프로세스시작시각UnixMs}</c>.
///
/// 서버 쪽 IdKeeper.Common.MachineConstant와 목적이 같지만 구현이 다르다. 그쪽은 Linux에서
/// /bin/sh로 grep·sed·awk를 실행하는데, <b>distroless 이미지에는 셸이 없어</b> 네 번의 프로세스
/// 생성이 모두 실패한 뒤 폴백된다. 남의 애플리케이션에 임베드되는 라이브러리가 기동할 때마다
/// 셸을 띄우는 건 이식성·보안 양쪽에서 부적절하므로, 여기서는 /proc 파일을 직접 읽는다.
/// </summary>
public static class SnowflakeClientIdentity
{
	private const Int32 MachineIdMaxLength = 64;

	private static readonly Lazy<string> s_current = new(Build, isThreadSafe: true);

	/// <summary>
	/// 프로세스 인스턴스마다 유일한 식별자. 최초 접근 시 한 번 산출되고 이후 캐시된다.
	/// </summary>
	public static string Current => s_current.Value;

	private static string Build()
		=> $"{GetMachineId()}|{Environment.ProcessId}|{GetProcessStartTimeUnixMs()}";

	private static string GetMachineId()
	{
		string? machineId = null;

		try
		{
			if (OperatingSystem.IsWindows())
			{
				machineId = GetWindowsMachineGuid();
			}
			else if (OperatingSystem.IsLinux())
			{
				machineId = GetKubernetesPodUid()
					?? GetDockerContainerId()
					?? ReadFirstLine("/etc/machine-id");
			}
			else if (OperatingSystem.IsMacOS())
			{
				// macOS의 IOPlatformUUID는 셸/ioreg 없이는 읽을 수 없다. 프로세스를 띄우지 않는다는
				// 원칙을 지키고 아래 폴백(부팅 시각 + 호스트명)을 쓴다 — PID와 프로세스 시작
				// 시각이 함께 붙으므로 유일성은 유지된다.
				machineId = null;
			}
		}
		catch
		{
			// 어떤 환경에서도 식별자 산출 실패가 기동을 막아서는 안 된다. 아래 폴백으로 넘어간다.
		}

		if (string.IsNullOrEmpty(machineId))
		{
			machineId = $"{GetBootTimeUtc()}|{Environment.MachineName}";
		}

		return machineId.Length > MachineIdMaxLength
			? machineId[..MachineIdMaxLength]
			: machineId;
	}

	[System.Runtime.Versioning.SupportedOSPlatform("windows")]
	private static string? GetWindowsMachineGuid()
	{
		// Microsoft.Win32.Registry 패키지 의존을 피하려고 레지스트리 대신 호스트명 기반 폴백을 쓴다.
		// Windows는 이 라이브러리의 주 배포 대상(리눅스 컨테이너)이 아니고, PID와 프로세스 시작
		// 시각이 함께 붙어 유일성은 유지된다.
		return null;
	}

	private static string? GetKubernetesPodUid()
	{
		// /proc/self/mountinfo 예: ... /var/lib/kubelet/pods/<uuid>/volumes/...
		// cgroup v2 환경에서는 mountinfo에 없을 수 있어 cgroup도 함께 본다.
		string? uid = MatchFirst("/proc/self/mountinfo",
			@"/pods/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})");
		if (uid is not null)
		{
			return uid;
		}

		// cgroup 표기는 하이픈이 밑줄인 경우가 있어(pod<uuid> 또는 pod_<uuid>) 둘 다 받는다.
		return MatchFirst("/proc/self/cgroup",
			@"pod[_-]?([0-9a-f]{8}[-_][0-9a-f]{4}[-_][0-9a-f]{4}[-_][0-9a-f]{4}[-_][0-9a-f]{12})");
	}

	private static string? GetDockerContainerId()
	{
		return MatchFirst("/proc/self/mountinfo", @"docker/containers/([0-9a-f]{12,})")
			?? MatchFirst("/proc/self/cgroup", @"docker[/-]([0-9a-f]{12,})");
	}

	private static string? MatchFirst(string path, string pattern)
	{
		try
		{
			if (!File.Exists(path))
			{
				return null;
			}

			// /proc 파일은 크기가 0으로 보고되지만 ReadAllText는 정상 동작한다.
			string content = File.ReadAllText(path);
			Match match = Regex.Match(content, pattern,
				RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
			return match.Success ? Normalize(match.Groups[1].Value) : null;
		}
		catch
		{
			return null;
		}
	}

	private static string? ReadFirstLine(string path)
	{
		try
		{
			if (!File.Exists(path))
			{
				return null;
			}

			using StreamReader reader = new(path);
			return Normalize(reader.ReadLine());
		}
		catch
		{
			return null;
		}
	}

	private static string? Normalize(string? value)
		=> string.IsNullOrWhiteSpace(value) ? null : value.Trim();

	private static string GetBootTimeUtc()
	{
		TimeSpan uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
		return $"{DateTimeOffset.UtcNow - uptime:yyyy-MM-ddTHH:mm:ss.fffzzz}";
	}

	private static string GetProcessStartTimeUnixMs()
	{
		try
		{
			using Process process = Process.GetCurrentProcess();
			return new DateTimeOffset(process.StartTime.ToUniversalTime())
				.ToUnixTimeMilliseconds().ToString();
		}
		catch
		{
			return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
		}
	}
}
