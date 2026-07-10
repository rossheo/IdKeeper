namespace IdKeeper.SnowflakeApiService.Responses;

public class SnowflakeIdResponseV1Alloc
{
	public required IReadOnlyList<Int64> Ids { get; set; }
}