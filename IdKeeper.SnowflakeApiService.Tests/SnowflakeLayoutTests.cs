using IdKeeper.Common.Constants;
using Xunit;

namespace IdKeeper.SnowflakeApiService.Tests;

public sealed class SnowflakeLayoutTests
{
	[Fact]
	public void Default_BitCounts_SumTo63()
	{
		SnowflakeLayout layout = SnowflakeConstant.Default;
		Int32 sum = layout.BitCountOfTimestamp + layout.BitCountOfNodeId + layout.BitCountOfSequenceId;

		Assert.Equal(63, sum);
	}

	[Fact]
	public void Default_MaxNodeIdInclusive_MatchesBitCount()
	{
		SnowflakeLayout layout = SnowflakeConstant.Default;
		Int32 actual = (1 << layout.BitCountOfNodeId) - 1;

		Assert.Equal(layout.MaxNodeIdInclusive, actual);
	}

	[Fact]
	public void Default_BaseDateTime_IsUtc()
	{
		Assert.Equal(DateTimeKind.Utc, SnowflakeConstant.Default.BaseDateTime.Kind);
	}

	[Fact]
	public void Default_EnsureValid_DoesNotThrow()
	{
		SnowflakeConstant.Default.EnsureValid();
	}

	[Fact]
	public void EnsureValid_WhenSumIsNot63_Throws()
	{
		SnowflakeLayout layout = SnowflakeConstant.Default with { BitCountOfNodeId = 13 };

		Assert.Throws<InvalidOperationException>(layout.EnsureValid);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(62)]
	public void EnsureValid_WhenBitCountOutOfRange_Throws(Int32 invalidBitCount)
	{
		SnowflakeLayout layout = SnowflakeConstant.Default with { BitCountOfTimestamp = invalidBitCount };

		Assert.Throws<InvalidOperationException>(layout.EnsureValid);
	}

	[Fact]
	public void EnsureValid_WhenTimestampBitsBelowMinimum_Throws()
	{
		// sum: 34+19+10=63 (합계는 유효, Timestamp 최소값(35)만 위반)
		SnowflakeLayout layout = SnowflakeConstant.Default with { BitCountOfTimestamp = 34, BitCountOfNodeId = 19 };

		Assert.Throws<InvalidOperationException>(layout.EnsureValid);
	}

	[Fact]
	public void EnsureValid_WhenTimestampBitsAtMinimum_DoesNotThrow()
	{
		// sum: 35+18+10=63
		SnowflakeLayout layout = SnowflakeConstant.Default with { BitCountOfTimestamp = 35, BitCountOfNodeId = 18 };

		layout.EnsureValid();
	}

	[Fact]
	public void EnsureValid_WhenStartYearBeforeMinimum_Throws()
	{
		SnowflakeLayout layout = SnowflakeConstant.Default with { BaseDateTimeStartYear = 1999 };

		Assert.Throws<InvalidOperationException>(layout.EnsureValid);
	}

	[Fact]
	public void EnsureValid_WhenStartYearInFuture_Throws()
	{
		SnowflakeLayout layout = SnowflakeConstant.Default with { BaseDateTimeStartYear = DateTime.UtcNow.Year + 1 };

		Assert.Throws<InvalidOperationException>(layout.EnsureValid);
	}
}
