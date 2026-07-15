namespace IdKeeper.Common.Constants;

public static class AuditLogAction
{
	public const string Alloc = "Alloc";
	public const string Renew = "Renew";
	public const string Remove = "Remove";
	public const string XApiKeyCreated = "XApiKeyCreated";
	public const string XApiKeyDeleted = "XApiKeyDeleted";
	public const string IgnoreExpireChanged = "IgnoreExpireChanged";
	public const string DescriptionUpdated = "DescriptionUpdated";
	public const string UserRoleChanged = "UserRoleChanged";
	public const string UserEmailConfirmedChanged = "UserEmailConfirmedChanged";
	public const string FeatureSwitchCreated = "FeatureSwitchCreated";
	public const string FeatureSwitchUpdated = "FeatureSwitchUpdated";
	public const string FeatureSwitchDeleted = "FeatureSwitchDeleted";
	public const string FeatureSwitchToggled = "FeatureSwitchToggled";
	public const string UserLockoutSet = "UserLockoutSet";
	public const string UserLockoutCleared = "UserLockoutCleared";
	public const string XApiKeyDescriptionUpdated = "XApiKeyDescriptionUpdated";
	public const string XApiKeyExpiredUpdated = "XApiKeyExpiredUpdated";
	public const string XApiWhitelistCreated = "XApiWhitelistCreated";
	public const string XApiWhitelistDeleted = "XApiWhitelistDeleted";
	public const string XApiWhitelistDescriptionUpdated = "XApiWhitelistDescriptionUpdated";
	public const string XApiWhitelistHostnameCreated = "XApiWhitelistHostnameCreated";
	public const string XApiWhitelistHostnameDeleted = "XApiWhitelistHostnameDeleted";
	public const string XApiWhitelistHostnameDescriptionUpdated = "XApiWhitelistHostnameDescriptionUpdated";
	public const string RedisBackupExported = "RedisBackupExported";
	public const string RedisBackupImported = "RedisBackupImported";
	public const string RedisBackupScheduleChanged = "RedisBackupScheduleChanged";
	public const string RedisBackupFileMetadataChanged = "RedisBackupFileMetadataChanged";
	public const string RedisMigrated = "RedisMigrated";
	public const string CredentialSettingsUpdated = "CredentialSettingsUpdated";
	public const string SnowflakeLayoutChanged = "SnowflakeLayoutChanged";
}
