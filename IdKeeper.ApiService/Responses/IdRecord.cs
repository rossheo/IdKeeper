namespace IdKeeper.ApiService.Responses;

public record IdRecord(Int32 Id, DateTimeOffset ExpiredAtUtc);
