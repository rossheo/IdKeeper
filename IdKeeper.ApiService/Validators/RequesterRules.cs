using FluentValidation;

namespace IdKeeper.ApiService.Validators;

internal static class RequesterRules
{
	public static IRuleBuilderOptions<T, string> RequesterRule<T>(
		this IRuleBuilder<T, string> rule) =>
		rule
			.NotEmpty()
			.WithMessage("Requester is required.")
			.MinimumLength(4)
			.WithMessage("Requester must be at least 4 characters long.")
			.MaximumLength(128)
			.WithMessage("Requester must not exceed 128 characters.");
}
