using DataTrustHub.Domain.Users;
using FluentValidation;

namespace DataTrustHub.Features.Administration.CreateAccount;

internal class CreateAccountValidator : AbstractValidator<CreateAccountCommand>
{
    public CreateAccountValidator()
    {
        // Format and length apply to the normalized address, so surrounding whitespace alone is not invalid (U7)
        RuleFor(x => x.Email)
            .NotEmpty()
            .DependentRules(() =>
                RuleFor(x => EmailAddress.From(x.Email).Value)
                    .EmailAddress()
                    .OverridePropertyName(nameof(CreateAccountCommand.Email))
                    .MaximumLength(Constants.MaxEmailLength));

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(Constants.MinPasswordLength);
    }
}
