using IdKeeper.Common.Constants;
using IdKeeper.Common.Extensions;
using IdKeeper.Database.Redis;
using IdKeeper.Database.Redis.Extensions;
using IdKeeper.Database.Redis.Identity;
using IdKeeper.Database.Redis.Repositories;
using IdKeeper.Web.Components;
using IdKeeper.Web.Components.Account;
using IdKeeper.Web.Endpoints;
using IdKeeper.Web.HostedServices;
using IdKeeper.Web.Jobs;
using IdKeeper.Web.Settings;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using StackExchange.Redis;
using TickerQ.DependencyInjection;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddSeqEndpoint(connectionName: "seq");

builder.AddIdKeeperRedis();

builder.Services.AddSetting<RedisBackupSetting>();
builder.Services.AddSingleton<RedisBackupDiskGate>();
builder.Services.AddSingleton<RedisBackupJob>();
builder.Services.AddSingleton<CredentialSettingsRepository>();
builder.Services.AddHttpClient(nameof(CapacityAlertJob));
builder.Services.AddSingleton<CapacityAlertJob>();
builder.Services.AddHttpClient(nameof(XApiKeyExpiryAlertJob));
builder.Services.AddSingleton<XApiKeyExpiryAlertJob>();
builder.Services.AddHttpClient(nameof(SnowflakeWraparoundAlertJob));
builder.Services.AddSingleton<SnowflakeWraparoundAlertJob>();
// SnowflakeLayoutSettings.razor의 Discord 알림 전송용 (Program.cs에서 razor 컴포넌트 타입을
// 직접 참조하지 않도록 리터럴 이름을 쓰고, 페이지 쪽은 nameof(SnowflakeLayoutSettings)로 동일한
// 이름을 참조한다).
builder.Services.AddHttpClient("SnowflakeLayoutSettings");
builder.Services.AddTickerQ();

// Data Protection 키를 Redis에 영속화 (컨테이너 재시작 시 키 유실 방지).
// IConnectionMultiplexer는 builder.Build() 이후 실제 컨테이너에서 지연 해석해야
// Aspire가 등록한 싱글톤과 별도의 커넥션이 생기지 않는다(IConfigureOptions로 지연).
builder.Services.AddDataProtection()
	.SetApplicationName("IdKeeper.Web");
builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
{
	IConnectionMultiplexer multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();
	RedisXmlRepository repository = new(() => multiplexer.GetDatabase(), RedisKeyNames.DataProtection.Keys);
	return new ConfigureOptions<KeyManagementOptions>(options => options.XmlRepository = repository);
});

builder.Services.AddMudServices();

builder.Services.AddRazorComponents()
	.AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
	{
		options.DefaultScheme = IdentityConstants.ApplicationScheme;
		options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
	})
	.AddIdentityCookies();

builder.Services.AddIdentityCore<IdentityUser>(options =>
{
	options.SignIn.RequireConfirmedAccount = false;
	options.SignIn.RequireConfirmedEmail = false;
	options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;

	options.Password.RequiredLength = 10;
	options.Password.RequireNonAlphanumeric = false;
	options.Password.RequireLowercase = false;
	options.Password.RequireUppercase = false;
	options.Password.RequireDigit = false;

	options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
	options.Lockout.MaxFailedAccessAttempts = 5;
	options.Lockout.AllowedForNewUsers = true;

	options.User.RequireUniqueEmail = true;
})
	.AddRoles<IdentityRole>()
	.AddUserStore<IdentityUserStore>()
	.AddRoleStore<IdentityRoleStore>()
	.AddSignInManager()
	.AddDefaultTokenProviders();

builder.Services.AddAuthorization();

builder.Services.AddSingleton<IEmailSender<IdentityUser>, IdentityNoOpEmailSender>();

builder.Services.AddHostedService<RedisSeedHostedService>();

builder.Services.AddSingleton<SnowflakeLayoutHolder>();

WebApplication app = builder.Build();
VersionConstant.Logging(app.Logger);
MachineConstant.Logging(app.Logger);

// SnowflakeLayoutHolder는 Redis 접근이 필요해 SnowflakeConstant.EnsureValid()의 정적 호출을
// 여기(app.Build() 이후, DI 준비 시점)로 대체한다. 값은 프로세스 생명주기 동안 고정 —
// 변경 후 반영하려면 재시작이 필요하다(/snowflakelayout 페이지 경고 참고).
await using (AsyncServiceScope snowflakeLayoutScope = app.Services.CreateAsyncScope())
{
	SnowflakeLayoutRepository snowflakeLayoutRepository =
		snowflakeLayoutScope.ServiceProvider.GetRequiredService<SnowflakeLayoutRepository>();
	SnowflakeLayout snowflakeLayout = await snowflakeLayoutRepository.GetAsync();
	snowflakeLayout.EnsureValid();
	snowflakeLayoutScope.ServiceProvider.GetRequiredService<SnowflakeLayoutHolder>().Initialize(snowflakeLayout);
}

if (!app.Environment.IsDevelopment())
{
	app.UseExceptionHandler("/Error", createScopeForErrors: true);
	app.UseHsts();
}
else
{
	app.UseDeveloperExceptionPage();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

if (app.Configuration.GetValue<bool>("UseHttpsRedirection"))
{
	app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.UseTickerQ();

app.MapStaticAssets();

app.MapDefaultEndpoints();

app.MapRazorComponents<App>()
	.AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.MapRedisBackupEndpoints();

app.Run();
