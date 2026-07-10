using IdKeeper.Common.Constants;
using Xunit;

namespace IdKeeper.SnowflakeApiService.Tests;

public sealed class SnowflakeConstantTests
{
	[Fact]
	public void BitCounts_SumTo63()
	{
		Int32 sum =
			SnowflakeConstant.BitCountOfTimestamp +
			SnowflakeConstant.BitCountOfNodeId +
			SnowflakeConstant.BitCountOfSequenceId;

		Assert.Equal(63, sum);
	}

	[Fact]
	public void MaxNodeIdInclusive_MatchesBitCount()
	{
		Int32 actual = (1 << SnowflakeConstant.BitCountOfNodeId) - 1;
		Assert.Equal(SnowflakeConstant.MaxNodeIdInclusive, actual);
	}

	[Fact]
	public void BaseDateTime_IsUtc()
	{
		Assert.Equal(DateTimeKind.Utc, SnowflakeConstant.BaseDateTime.Kind);
	}

	[Fact]
	public void EnsureValid_DoesNotThrow()
	{
		SnowflakeConstant.EnsureValid();
	}
}
