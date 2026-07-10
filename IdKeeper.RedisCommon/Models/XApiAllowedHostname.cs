namespace IdKeeper.Database.Redis.Models;

/// <summary>Hostname 자체가 자연키(natural key)라 별도 Id가 없다.</summary>
public class XApiAllowedHostname
{
	public string Hostname { get; set; } = string.Empty;
	public string? Description { get; set; }
}
