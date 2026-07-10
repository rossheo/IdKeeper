using FluentValidation;
using IdKeeper.ApiService.Requests;

namespace IdKeeper.ApiService.Validators;

public class IdKeeperRequestV1RemoveValidator : AbstractValidator<IdKeeperRequestV1Remove>
{
	public IdKeeperRequestV1RemoveValidator()
	{
		RuleFor(x => x.Requester).RequesterRule();
	}
}
