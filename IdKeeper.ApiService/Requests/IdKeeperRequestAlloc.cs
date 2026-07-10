namespace IdKeeper.ApiService.Requests;

public class IdKeeperRequestV1Alloc
{
	public Int32 Count { get; set; }
	public string Requester { get; set; } = string.Empty;
}
