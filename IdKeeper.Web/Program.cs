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

SnowflakeConstant.EnsureValid();

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

WebApplication app = builder.Build();
VersionConstant.Logging(app.Logger);
MachineConstant.Logging(app.Logger);

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
