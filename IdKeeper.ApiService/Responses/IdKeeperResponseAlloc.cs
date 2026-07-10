namespace IdKeeper.ApiService.Responses;

public class IdKeeperResponseV1Alloc
{
	public record BitCountRecord(Int32 Timestamp, Int32 NodeId, Int32 SequenceId);

	public required DateTimeOffset BaseDateTime { get; set; }

	public required BitCountRecord BitCount { get; set; }
	public required List<IdRecord> Ids { get; set; }
}
