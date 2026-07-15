namespace IdKeeper.Database.Redis;

/// <summary>
/// 모든 Redis 키는 "IdKeeper/" 하위 프리픽스만 사용한다(root 키 금지).
/// 엔티티별 해시태그({...})는 향후 Redis Cluster(샤딩) 확장 시 같은 엔티티의 키들이
/// 항상 같은 슬롯에 배치되도록 보장한다(단일 인스턴스에서는 무의미하지만 선제 대응).
/// </summary>
public static class RedisKeyNames
{
	public static class AllocatedId
	{
		private const string Tag = "{AllocatedId}";

		public static string Bitmap => $"IdKeeper/AllocatedId/{Tag}/Bitmap";
		public static string ExpiryIndex => $"IdKeeper/AllocatedId/{Tag}/ExpiryIndex";
		public static string Entry(Int32 id) => $"IdKeeper/AllocatedId/{Tag}/{id}";
		public static string ByRequester(string requester) => $"IdKeeper/AllocatedId/{Tag}/ByRequester/{requester}";
	}

	public static class XApiKey
	{
		private const string Tag = "{XApiKey}";

		public static string Seq => $"IdKeeper/XApiKey/{Tag}/Seq";
		public static string Entry(Int64 id) => $"IdKeeper/XApiKey/{Tag}/{id}";
		public static string ByApiKey(string apiKey) => $"IdKeeper/XApiKey/{Tag}/ByApiKey/{apiKey}";
		public static string All => $"IdKeeper/XApiKey/{Tag}/All";
	}

	public static class XApiAllowedCidr
	{
		private const string Tag = "{XApiAllowedCidr}";

		public static string Entry(string cidr) => $"IdKeeper/XApiAllowedCidr/{Tag}/{cidr}";
		public static string All => $"IdKeeper/XApiAllowedCidr/{Tag}/All";
	}

	public static class XApiAllowedHostname
	{
		private const string Tag = "{XApiAllowedHostname}";

		public static string Entry(string hostname) => $"IdKeeper/XApiAllowedHostname/{Tag}/{hostname}";
		public static string All => $"IdKeeper/XApiAllowedHostname/{Tag}/All";
	}

	public static class FeatureSwitch
	{
		private const string Tag = "{FeatureSwitch}";

		public static string Entry(string key) => $"IdKeeper/FeatureSwitch/{Tag}/{key}";
		public static string All => $"IdKeeper/FeatureSwitch/{Tag}/All";
	}

	public static class AuditLog
	{
		private const string Tag = "{AuditLog}";

		public static string Seq => $"IdKeeper/AuditLog/{Tag}/Seq";
		public static string Entry(Int64 id) => $"IdKeeper/AuditLog/{Tag}/{id}";
		public static string All => $"IdKeeper/AuditLog/{Tag}/All";
	}

	public static class Identity
	{
		private const string UserTag = "{IdentityUser}";
		private const string RoleTag = "{IdentityRole}";

		public static string User(string userId) => $"IdKeeper/Identity/User/{UserTag}/{userId}";
		public static string UserByNormalizedUserName(string normalizedUserName) =>
			$"IdKeeper/Identity/User/{UserTag}/ByNormalizedUserName/{normalizedUserName}";
		public static string UserByNormalizedEmail(string normalizedEmail) =>
			$"IdKeeper/Identity/User/{UserTag}/ByNormalizedEmail/{normalizedEmail}";
		public static string UserAll => $"IdKeeper/Identity/User/{UserTag}/All";
		public static string UserRoles(string userId) => $"IdKeeper/Identity/User/{UserTag}/Roles/{userId}";

		public static string Role(string roleId) => $"IdKeeper/Identity/Role/{RoleTag}/{roleId}";
		public static string RoleByNormalizedName(string normalizedName) =>
			$"IdKeeper/Identity/Role/{RoleTag}/ByNormalizedName/{normalizedName}";
		public static string RoleUsers(string roleName) => $"IdKeeper/Identity/Role/{RoleTag}/Users/{roleName}";
	}

	public static class DataProtection
	{
		public static string Keys => "IdKeeper/DataProtection/Keys";
	}

	public static class PubSub
	{
		public static string CidrChanged => "IdKeeper/PubSub/CidrChanged";
	}

	public static class Lock
	{
		public static string CleanupExpiredJob => "IdKeeper/Lock/Jobs/CleanupExpired";
		public static string CleanupAuditLogJob => "IdKeeper/Lock/Jobs/CleanupAuditLog";
	}

	public static class RedisBackupSchedule
	{
		public static string Settings => "IdKeeper/RedisBackupSchedule/Settings";
	}

	public static class SnowflakeLayout
	{
		public static string Settings => "IdKeeper/SnowflakeLayout/Settings";
	}

	public static class CredentialSettings
	{
		private const string Tag = "{CredentialSettings}";

		public static string Entry(string userId) => $"IdKeeper/CredentialSettings/{Tag}/{userId}";
		public static string DiscordConfiguredUserIds => $"IdKeeper/CredentialSettings/{Tag}/DiscordConfiguredUserIds";
	}

	/// <summary>export/import 도구가 이 리포지토리(공유 Redis 인스턴스)에 속한 모든 키를 한정하는 패턴.</summary>
	public const string AllKeysPattern = "IdKeeper/*";
}
