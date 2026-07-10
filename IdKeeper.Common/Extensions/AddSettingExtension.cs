using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace IdKeeper.Common.Extensions;

public static class AddSettingExtension
{
	public static IServiceCollection AddSetting<TSetting>(this IServiceCollection services)
		where TSetting : class, new()
	{
		ArgumentNullException.ThrowIfNull(services);

		services
			.AddOptions<TSetting>()
			.BindConfiguration(typeof(TSetting).Name)
			.ValidateDataAnnotations()
			.ValidateOnStart();

		RegisterSettingSingleton<TSetting>(services);

		return services;
	}

	public static IServiceCollection AddSetting<TSetting>(
		this IServiceCollection services, Action<TSetting> configure) where TSetting : class, new()
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configure);

		services
			.AddOptions<TSetting>()
			.BindConfiguration(typeof(TSetting).Name)
			.Configure(configure)
			.ValidateDataAnnotations()
			.ValidateOnStart();

		RegisterSettingSingleton<TSetting>(services);

		return services;
	}

	private static void RegisterSettingSingleton<TSetting>(IServiceCollection services)
		where TSetting : class
	{
		services.AddSingleton(serviceProvider =>
		{
			IOptionsMonitor<TSetting> monitor =
				serviceProvider.GetRequiredService<IOptionsMonitor<TSetting>>();
			TSetting instance = monitor.CurrentValue;

			monitor.OnChange(newValue => CopyProperties(instance, newValue));

			return instance;
		});
	}

	private static void CopyProperties<TSetting>(TSetting target, TSetting source)
		where TSetting : class
	{
		foreach (PropertyInfo prop in typeof(TSetting)
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(p => p.CanWrite))
		{
			prop.SetValue(target, prop.GetValue(source));
		}
	}
}