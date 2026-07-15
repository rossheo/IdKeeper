using IdKeeper.Common.Constants;
using Microsoft.Extensions.Configuration;
using System.ComponentModel.DataAnnotations;

namespace IdKeeper.SnowflakeApiService.Settings;

public sealed class SnowflakeSetting : IValidatableObject
{
	[MaxLength(53)]
	public string? IdKeeperApiKey { get; set; }

	public TimeSpan RenewLoopDuration { get; set; } = TimeSpan.FromMinutes(10);

	public Int32 GeneratorCount { get; set; } = 1;

	public void ApplyEnvironmentVariables(IConfiguration configuration)
	{
		string apiKey = configuration["IDKEEPER_APIKEY"] ?? string.Empty;
		if (!string.IsNullOrEmpty(apiKey))
			IdKeeperApiKey = apiKey;

		string countStr = configuration["IDKEEPER_GENERATOR_COUNT"] ?? string.Empty;
		if (!string.IsNullOrEmpty(countStr) && Int32.TryParse(countStr, out Int32 count))
			GeneratorCount = count;
	}

	public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
	{
		if (!string.IsNullOrEmpty(IdKeeperApiKey))
		{
			if (IdKeeperApiKey.Length < 9)
			{
				yield return new ValidationResult(
					$"'{nameof(IdKeeperApiKey)}' must be at least 9 characters when provided.",
					[nameof(IdKeeperApiKey)]);
			}
			if (!IdKeeperApiKey.StartsWith(XApiKeyConstant.XApiKeyPrefix, StringComparison.Ordinal))
			{
				yield return new ValidationResult(
					$"'{nameof(IdKeeperApiKey)}' must start with '{XApiKeyConstant.XApiKeyPrefix}'.",
					[nameof(IdKeeperApiKey)]);
			}
		}

		if (RenewLoopDuration <= TimeSpan.Zero || RenewLoopDuration > TimeSpan.FromMinutes(30))
		{
			yield return new ValidationResult(
				$"'{nameof(RenewLoopDuration)}' must be between 1 second and 30 minutes." +
				" (30 minutes is safe with the minimum LeaseDuration of 50 minutes.)",
				[nameof(RenewLoopDuration)]);
		}

		// 이 프로젝트는 Redis에 접근하지 못해 실제(동적) 레이아웃을 알 수 없다 — 여기서는
		// 기본 레이아웃 기준의 사전 검증(sanity bound)만 수행하고, 실제 상한은 Alloc 응답의
		// BitCount로 SnowflakeHostedService.InitializeAsync에서 검증한다.
		Int32 maxGeneratorCount = SnowflakeConstant.Default.MaxNodeIdInclusive + 1;
		if (GeneratorCount < 1 || GeneratorCount > maxGeneratorCount)
		{
			yield return new ValidationResult(
				$"'{nameof(GeneratorCount)}' must be between 1 and {maxGeneratorCount}.",
				[nameof(GeneratorCount)]);
		}
	}
}