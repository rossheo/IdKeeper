namespace IdKeeper.Database.Redis.Models;

public class AuditLog
{
	public Int64 Id { get; set; }
	public string Action { get; set; } = string.Empty;
	public string Actor { get; set; } = string.Empty;
	public string? Requester { get; set; }
	public string? AffectedIds { get; set; }
	public string? RemoteIp { get; set; }
	public string? Detail { get; set; }
	public DateTime CreatedAtUtc { get; set; }
}
