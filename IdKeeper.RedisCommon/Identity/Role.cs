namespace IdKeeper.Database.Redis.Identity;

public static class Role
{
	public const string Administrator = "Administrator";
	public const string Maintainer = "Maintainer"; // Can manage data but not users
	public const string Guest = "Guest";
}