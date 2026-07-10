using System.Collections.Concurrent;
using System.Reflection;

namespace IdKeeper.Database.Redis.Scripts;

/// <summary>
/// Scripts/*.lua로 임베드된 원자적 Lua 스크립트를 로드/캐시한다.
/// StackExchange.Redis의 ScriptEvaluateAsync는 내부적으로 EVALSHA 캐싱을 처리하므로
/// 이 로더는 스크립트 원문 텍스트만 프로세스 내 캐싱한다.
/// </summary>
public sealed class LuaScriptLoader
{
	private readonly ConcurrentDictionary<string, string> _scripts = new();
	private static readonly Assembly ResourceAssembly = typeof(LuaScriptLoader).Assembly;

	public string Load(string scriptName)
	{
		return _scripts.GetOrAdd(scriptName, name =>
		{
			string resourceName = $"{ResourceAssembly.GetName().Name}.Scripts.{name}.lua";
			using Stream? stream = ResourceAssembly.GetManifestResourceStream(resourceName);
			if (stream is null)
			{
				throw new InvalidOperationException($"Embedded Lua script not found: {resourceName}");
			}

			using StreamReader reader = new(stream);
			return reader.ReadToEnd();
		});
	}
}
