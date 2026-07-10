namespace IdKeeper.Database.Redis.Models;

/// <summary>Cidr 자체가 자연키(natural key)라 별도 Id가 없다.</summary>
public class XApiAllowedCidr
{
	public string Cidr { get; set; } = string.Empty;
	public string? Description { get; set; }
}
