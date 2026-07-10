using FluentValidation;
using IdKeeper.ApiService.Requests;
using IdKeeper.ApiService.Settings;

namespace IdKeeper.ApiService.Validators;

public class IdKeeperRequestV1AllocValidator : AbstractValidator<IdKeeperRequestV1Alloc>
{
	public IdKeeperRequestV1AllocValidator(IdKeeperSetting setting)
	{
		RuleFor(x => x.Count)
			.GreaterThan(0)
			.WithMessage("Count must be greater than 0.")
			.LessThanOrEqualTo(setting.MaxAllocCount)
			.WithMessage($"Count must not exceed {setting.MaxAllocCount}.");

		RuleFor(x => x.Requester).RequesterRule();
	}
}
