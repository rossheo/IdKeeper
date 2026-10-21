using System.Collections.Concurrent;
using System.Reflection;

namespace IdKeeper.Common.Constants;

public static class ReflectionConstant
{
	public static string[] GetPublicStaticStringMemberValues(Type type)
	{
		if (type is null)
		{
			throw new ArgumentNullException(nameof(type));
		}

		return PublicStaticStringCache.Get(type);
	}

	internal static class PublicStaticStringCache
	{
		private static readonly ConcurrentDictionary<Type, string[]> _cache = new();

		internal static string[] Get(Type type) => _cache.GetOrAdd(type, Collect);

		private static string[] Collect(Type type)
		{
			HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

			static void AddIfValid(HashSet<string> set, object? value)
			{
				if (value is string s && !string.IsNullOrWhiteSpace(s))
				{
					set.Add(s);
				}
			}

			const BindingFlags flags =
				BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;

			foreach (FieldInfo fieldInfo in type.GetFields(flags))
			{
				if (fieldInfo.FieldType != typeof(string)) continue;

				try
				{
					object? val = fieldInfo.IsLiteral
						? fieldInfo.GetRawConstantValue()
						: fieldInfo.GetValue(null);

					AddIfValid(names, val);
				}
				catch
				{
				}
			}

			foreach (PropertyInfo pi in type.GetProperties(flags))
			{
				if (pi.PropertyType != typeof(string))
				{
					continue;
				}

				MethodInfo? getter = pi.GetMethod;
				if (getter is null || !getter.IsPublic || !getter.IsStatic)
				{
					continue;
				}

				if (pi.GetIndexParameters().Length != 0)
				{
					continue;
				}

				try
				{
					object? val = getter.Invoke(null, null);
					AddIfValid(names, val);
				}
				catch
				{
				}
			}

			if (names.Count == 0)
			{
				return [];
			}

			return [.. names.OrderBy(s => s, StringComparer.Ordinal)];
		}
	}
}