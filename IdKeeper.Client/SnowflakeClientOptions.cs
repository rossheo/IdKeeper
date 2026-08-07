using System.ComponentModel.DataAnnotations;

namespace IdKeeper.Client;

/// <summary>
/// IdKeeper 클라이언트 설정. AddIdKeeperSnowflake에서 구성하며, 기동 시 검증되어
/// 잘못된 값이면 애플리케이션이 즉시 실패한다.
/// </summary>
public sealed class SnowflakeClientOptions : IValidatableObject
{
	/// <summary>
	/// IdKeeper 서버의 주소 (예: https://idkeeper.internal). 필수.
	///
	/// 이전에는 Aspire 서비스 디스커버리가 "http://apiservice" 같은 논리 이름을 해석해 줬지만,
	/// 라이브러리 소비자는 Aspire를 쓰지 않을 수 있으므로 실제 주소를 받는다.
	/// </summary>
	[Required]
	public Uri? BaseAddress { get; set; }

	/// <summary>IdKeeper 서버가 발급한 X-API 키. 필수.</summary>
	[Required]
	public string? ApiKey { get; set; }

	/// <summary>
	/// 이 프로세스가 임대받을 노드 Id 개수. 기본값 1.
	///
	/// 임베디드로 쓰이면 소비 앱 인스턴스 수만큼 곱해지므로 기본값을 낮게 잡는다.
	/// 1ms당 발급 상한(레이아웃 기본값 기준 노드당 1,024개)을 넘겨야 할 때만 늘린다.
	/// </summary>
	public Int32 GeneratorCount { get; set; } = 1;

	/// <summary>임대 갱신 루프의 확인 주기. 기본값 10분.</summary>
	public TimeSpan RenewLoopDuration { get; set; } = TimeSpan.FromMinutes(10);

	/// <summary>
	/// 서버에 자신을 식별시키는 값. 비워 두면 <see cref="SnowflakeClientIdentity"/>가 자동으로
	/// 산출한다(머신 Id + PID + 프로세스 시작 시각).
	///
	/// <b>경고</b>: 이 값은 <b>프로세스 인스턴스마다 반드시 달라야 한다</b>. 서버의 Alloc은 같은
	/// 값으로 다시 요청하면 기존에 할당된 노드 Id를 그대로 돌려주는 멱등 동작을 한다(응답 유실
	/// 재시도 복구용). 호스트명·서비스명처럼 여러 프로세스가 공유하는 값을 넣으면 서로 다른
	/// 프로세스가 같은 노드 Id를 받아 <b>SnowflakeId가 중복된다</b>. 특별한 이유가 없으면 비워 둔다.
	/// </summary>
	public string? Requester { get; set; }

	/// <summary>DataAnnotations로 표현할 수 없는 교차 검증. 기동 시 한 번 호출된다.</summary>
	public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
	{
		if (BaseAddress is not null && !BaseAddress.IsAbsoluteUri)
		{
			yield return new ValidationResult(
				$"'{nameof(BaseAddress)}' must be an absolute URI.", [nameof(BaseAddress)]);
		}

		if (!string.IsNullOrEmpty(ApiKey))
		{
			if (ApiKey.Length < 9)
			{
				yield return new ValidationResult(
					$"'{nameof(ApiKey)}' must be at least 9 characters.", [nameof(ApiKey)]);
			}
			if (!ApiKey.StartsWith(XApiKeyConstant.XApiKeyPrefix, StringComparison.Ordinal))
			{
				yield return new ValidationResult(
					$"'{nameof(ApiKey)}' must start with '{XApiKeyConstant.XApiKeyPrefix}'.",
					[nameof(ApiKey)]);
			}
		}

		if (RenewLoopDuration <= TimeSpan.Zero || RenewLoopDuration > TimeSpan.FromMinutes(30))
		{
			yield return new ValidationResult(
				$"'{nameof(RenewLoopDuration)}' must be between 1 second and 30 minutes." +
				" (30 minutes is safe with the minimum LeaseDuration of 50 minutes.)",
				[nameof(RenewLoopDuration)]);
		}

		// 실제 상한은 서버가 응답하는 BitCount로 결정되므로 여기서는 사전 검증(sanity bound)만 한다.
		// 서버 레이아웃이 기본값보다 작을 수도 있어, 최종 검증은 SnowflakeHostedService가 Alloc
		// 응답을 받은 뒤 수행한다.
		const Int32 MaxGeneratorCount = 4096;
		if (GeneratorCount < 1 || GeneratorCount > MaxGeneratorCount)
		{
			yield return new ValidationResult(
				$"'{nameof(GeneratorCount)}' must be between 1 and {MaxGeneratorCount}.",
				[nameof(GeneratorCount)]);
		}

		if (Requester is not null && string.IsNullOrWhiteSpace(Requester))
		{
			yield return new ValidationResult(
				$"'{nameof(Requester)}' must not be blank when specified.", [nameof(Requester)]);
		}
	}
}
