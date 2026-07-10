namespace IdKeeper.Database.Redis.Models;

public class AllocatedId
{
	public Int32 Id { get; set; }
	public string Requester { get; set; } = string.Empty;
	public DateTime CreatedAtUtc { get; set; }
	public DateTime? UpdatedAtUtc { get; set; }
	public DateTime ExpiredAtUtc { get; set; }
	public bool IgnoreExpire { get; set; }
	public string? Description { get; set; }
}
