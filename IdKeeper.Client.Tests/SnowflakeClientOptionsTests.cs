using System.ComponentModel.DataAnnotations;
using Xunit;

namespace IdKeeper.Client.Tests;

/// <summary>
/// 기동 시 옵션 검증(ValidateDataAnnotations + ValidateOnStart)이 실제로 잘못된 값을
/// 걸러내는지 고정한다. 검증 메시지가 약속한 범위와 실제 검사가 어긋나면 오설정이
/// 기동을 통과해 런타임 동작으로 새어 나온다.
/// </summary>
public sealed class SnowflakeClientOptionsTests
{
	private static SnowflakeClientOptions Valid() => new()
	{
		BaseAddress = new Uri("http://idkeeper.test/"),
		ApiKey = "idkeeper-test-key",
	};

	private static IReadOnlyList<ValidationResult> Validate(SnowflakeClientOptions options)
		=> [.. options.Validate(new ValidationContext(options))];

	[Fact]
	public void Validate_DefaultOptions_HasNoErrors()
	{
		Assert.Empty(Validate(Valid()));
	}

	/// <summary>
	/// NextLoopDelay는 갱신 시점 전이면 RenewLoopDuration을 클램프 없이 그대로 대기 시간으로
	/// 쓴다. 1초 미만을 통과시키면 갱신 루프가 사실상 busy loop가 되므로, 검증 메시지가
	/// 약속하는 하한("between 1 second and 30 minutes")을 실제로 강제해야 한다.
	/// </summary>
	[Theory]
	[InlineData(0)]             // 0 — 항상 거부되어야 한다
	[InlineData(1)]             // 1 tick — 수정 전에는 통과했다
	[InlineData(9_990_000)]     // 999ms — 하한 바로 아래
	public void Validate_RenewLoopDurationBelowOneSecond_IsRejected(Int64 ticks)
	{
		SnowflakeClientOptions options = Valid();
		options.RenewLoopDuration = TimeSpan.FromTicks(ticks);

		Assert.Contains(
			Validate(options),
			r => r.MemberNames.Contains(nameof(SnowflakeClientOptions.RenewLoopDuration)));
	}

	[Theory]
	[InlineData(1)]
	[InlineData(600)]
	[InlineData(1800)]      // 30분 — 상한 경계는 허용
	public void Validate_RenewLoopDurationWithinBounds_IsAccepted(Int32 seconds)
	{
		SnowflakeClientOptions options = Valid();
		options.RenewLoopDuration = TimeSpan.FromSeconds(seconds);

		Assert.Empty(Validate(options));
	}

	[Fact]
	public void Validate_RenewLoopDurationAboveUpperBound_IsRejected()
	{
		SnowflakeClientOptions options = Valid();
		options.RenewLoopDuration = TimeSpan.FromMinutes(31);

		Assert.Contains(
			Validate(options),
			r => r.MemberNames.Contains(nameof(SnowflakeClientOptions.RenewLoopDuration)));
	}
}
