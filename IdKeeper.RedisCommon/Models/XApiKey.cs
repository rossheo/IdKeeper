namespace IdKeeper.Database.Redis.Models;

public class XApiKey
{
	public Int64 Id { get; set; }
	public string ApiKey { get; set; } = string.Empty;
	public string Owner { get; set; } = string.Empty;
	public string? Description { get; set; }
	public DateTime CreatedAtUtc { get; set; }
	public DateTime? ExpiredAtUtc { get; set; }
}
