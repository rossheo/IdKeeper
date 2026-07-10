using FluentValidation;
using IdKeeper.ApiService.Requests;

namespace IdKeeper.ApiService.Validators;

public class IdKeeperRequestV1RenewValidator : AbstractValidator<IdKeeperRequestV1Renew>
{
	public IdKeeperRequestV1RenewValidator()
	{
		RuleFor(x => x.Requester).RequesterRule();
	}
}
