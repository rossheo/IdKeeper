using IdKeeper.Client.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IdKeeper.Client;

/// <summary>IdKeeper 클라이언트를 DI에 등록하는 확장 메서드.</summary>
public static class ServiceCollectionExtensions
{
	/// <summary>
	/// IdKeeper에서 노드 Id를 임대받아 프로세스 내에서 SnowflakeId를 발급하는 클라이언트를 등록한다.
	/// 등록 후 <see cref="ISnowflakeIdGenerator"/>를 주입받아 사용한다.
	///
	/// <b>여러 번 호출해도 한 번만 등록된다.</b> 한 프로세스에 발급기가 둘 이상 있으면 안 되기
	/// 때문이다 — 서버의 Alloc은 같은 requester에게 <b>같은 노드 Id</b>를 돌려주는 멱등 동작을 하므로,
	/// 제너레이터 세트가 둘이면 각자의 시퀀스 카운터가 독립적으로 돌아 <b>완전히 동일한 ID</b>가
	/// 생성된다. DI로 막지 못하는 경우(한 프로세스에 호스트를 둘 띄우는 등)는 기동 시 예외로 막는다.
	/// </summary>
	public static IServiceCollection AddIdKeeperSnowflake(
		this IServiceCollection services, Action<SnowflakeClientOptions> configure)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configure);

		// 중복 호출 차단. AddHttpClient는 TryAdd 계열이 없어 두 번 부르면 등록이 쌓이므로,
		// 개별 등록에 기대지 않고 메서드 전체를 멱등하게 만든다.
		if (services.Any(d => d.ServiceType == typeof(SnowflakeHostedService)))
		{
			return services;
		}

		services.AddOptions<SnowflakeClientOptions>()
			.Configure(configure)
			.ValidateDataAnnotations()
			.ValidateOnStart();

		// 생성자가 IOptions<>가 아니라 값 타입을 직접 받도록 싱글턴으로 한 번 풀어 둔다.
		services.TryAddSingleton(serviceProvider =>
			serviceProvider.GetRequiredService<IOptions<SnowflakeClientOptions>>().Value);

		services.AddHttpClient<IdKeeperApiClient>((serviceProvider, httpClient) =>
		{
			SnowflakeClientOptions options =
				serviceProvider.GetRequiredService<SnowflakeClientOptions>();
			// ValidateOnStart가 이미 필수 여부를 검증하므로 여기 도달하면 null이 아니다.
			httpClient.BaseAddress = options.BaseAddress;
		})
		// Aspire의 ServiceDefaults가 걸어주던 것을 라이브러리가 직접 가져온다 — 소비자가 Aspire를
		// 쓴다는 보장이 없다. 전송 계층의 일시 실패는 이 핸들러가, 임대 획득 실패는
		// SnowflakeHostedService의 백오프가 담당한다.
		.AddStandardResilienceHandler();

		services.TryAddSingleton<SnowflakeHostedService>();
		services.TryAddSingleton<ISnowflakeIdGenerator>(serviceProvider =>
			serviceProvider.GetRequiredService<SnowflakeHostedService>());

		// 구현 타입을 명시하는 오버로드를 써야 TryAddEnumerable이 (IHostedService,
		// SnowflakeHostedService) 쌍으로 중복을 제거할 수 있다.
		services.TryAddEnumerable(
			ServiceDescriptor.Singleton<IHostedService, SnowflakeHostedService>(
				serviceProvider => serviceProvider.GetRequiredService<SnowflakeHostedService>()));

		return services;
	}

	/// <summary>
	/// 임대 상태를 헬스체크로 노출한다. 슬롯이 준비되고 임대가 유효할 때만 Healthy다.
	/// </summary>
	public static IHealthChecksBuilder AddIdKeeperSnowflake(
		this IHealthChecksBuilder builder,
		string name = "idkeeper-snowflake",
		HealthStatus? failureStatus = null,
		IEnumerable<string>? tags = null)
	{
		ArgumentNullException.ThrowIfNull(builder);

		return builder.AddCheck<SnowflakeInitHealthCheck>(name, failureStatus, tags ?? []);
	}
}
