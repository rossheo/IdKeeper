namespace IdKeeper.Database.Redis.Models;

/// <summary>Key 자체가 자연키(natural key)라 별도 Id가 없다.</summary>
public class FeatureSwitch
{
	public string Key { get; set; } = string.Empty;
	public bool IsEnabled { get; set; }
	public string? Description { get; set; }
}
