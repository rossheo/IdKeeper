using IdKeeper.SnowflakeApiService.HostedServices;
using System.ComponentModel.DataAnnotations;

namespace IdKeeper.SnowflakeApiService.Requests;

public class SnowflakeIdRequestV1Alloc
{
	[Required, Range(1, SnowflakeHostedService.MaxAllocateCount)]
	public Int32 Count { get; set; }
}
