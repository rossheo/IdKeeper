using IdKeeper.Database.Redis.Extensions;
using IdKeeper.Database.Redis.Models;
using Microsoft.AspNetCore.DataProtection;
using StackExchange.Redis;

namespace IdKeeper.Database.Redis.Repositories;

public sealed class CredentialSettingsRepository(
	IConnectionMultiplexer multiplexer, IDataProtectionProvider dataProtectionProvider)
{
	private readonly IDataProtector _protector =
		dataProtectionProvider.CreateProtector("IdKeeper.CredentialSettings.DiscordWebhook");

	private IDatabase Db => multiplexer.GetDatabase();

	public async Task<CredentialSettings> GetAsync(string userId, CancellationToken cancellationToken = default)
	{
		HashEntry[] entries = await Db.HashGetAllAsync(RedisKeyNames.CredentialSettings.Entry(userId));
		if (entries.Length == 0)
		{
			return new CredentialSettings();
		}

		Dictionary<string, string> fields = entries.ToDictionary(e => (string)e.Name!, e => (string)e.Value!);
		return new CredentialSettings
		{
			DiscordWebhookConfigured = !string.IsNullOrEmpty(fields.GetValueOrDefault("DiscordWebhookUrlProtected")),
			LastSavedAtUtc = fields.GetValueOrDefault("LastSavedAtUtc").ToUtcDateTimeOrNull(),
		};
	}

	/// <summary>
	/// webhookUrl이 빈칸이면 기존 저장 값을 그대로 유지하며 아무 것도 쓰지 않는다("빈칸 = 기존 값 유지" UX).
	/// 값이 실제로 갱신될 때만 true를 반환한다.
	/// </summary>
	public async Task<bool> SetDiscordWebhookAsync(
		string userId, string? webhookUrl, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(webhookUrl))
		{
			return false;
		}

		await Db.HashSetAsync(RedisKeyNames.CredentialSettings.Entry(userId),
		[
			new("DiscordWebhookUrlProtected", _protector.Protect(webhookUrl)),
			new("LastSavedAtUtc", DateTime.UtcNow.ToUnixSeconds()),
		]);
		await Db.SetAddAsync(RedisKeyNames.CredentialSettings.DiscordConfiguredUserIds, userId);
		return true;
	}

	/// <summary>실제 웹훅 전송 시에만 사용한다. UI에는 절대 복호화 값을 노출하지 않는다.</summary>
	public async Task<string?> GetDiscordWebhookUrlAsync(string userId, CancellationToken cancellationToken = default)
	{
		RedisValue protectedValue = await Db.HashGetAsync(
			RedisKeyNames.CredentialSettings.Entry(userId), "DiscordWebhookUrlProtected");
		return protectedValue.IsNullOrEmpty ? null : _protector.Unprotect((string)protectedValue!);
	}

	/// <summary>Discord 웹훅을 설정한 모든 사용자의 복호화된 웹훅 URL 목록. 알림 발송 시에만 사용한다.</summary>
	public async Task<List<string>> GetAllDiscordWebhookUrlsAsync(CancellationToken cancellationToken = default)
	{
		RedisValue[] userIds = await Db.SetMembersAsync(RedisKeyNames.CredentialSettings.DiscordConfiguredUserIds);
		List<string> urls = [];
		foreach (RedisValue userId in userIds)
		{
			string? url = await GetDiscordWebhookUrlAsync((string)userId!, cancellationToken);
			if (url is not null)
			{
				urls.Add(url);
			}
		}
		return urls;
	}
}
