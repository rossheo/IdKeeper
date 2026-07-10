namespace IdKeeper.Database.Redis.Models;

public class CredentialSettings
{
	public bool DiscordWebhookConfigured { get; set; }
	public DateTime? LastSavedAtUtc { get; set; }
}
