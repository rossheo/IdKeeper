using Google.Protobuf;
using IdKeeper.SnowflakeApiService.Responses;
using IdKeeper.SnowflakeApiService.SegmentedIntegers;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;

namespace IdKeeper.SnowflakeApiService.Formatters;

public sealed class ProtobufBlockedIntegerOutputFormatter : OutputFormatter
{
	public ProtobufBlockedIntegerOutputFormatter()
	{
		SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/x-protobuf"));
	}

	protected override Boolean CanWriteType(Type? type)
		=> type == typeof(SnowflakeIdResponseV1Alloc);

	public override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context)
	{
		if (context.Object is not SnowflakeIdResponseV1Alloc response)
			return;

		Pb.BlockedInteger proto = BlockedInteger.Encode(response.Ids);

		byte[] bytes = proto.ToByteArray();
		context.HttpContext.Response.ContentLength = bytes.Length;
		await context.HttpContext.Response.Body.WriteAsync(bytes);
	}
}
