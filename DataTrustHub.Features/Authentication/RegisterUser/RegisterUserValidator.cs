using DataTrustHub.Domain.Users;
using FluentValidation;

namespace DataTrustHub.Features.Authentication.RegisterUser;

internal class RegisterUserValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserValidator()
    {
        // Format and length apply to the normalized address, so surrounding whitespace alone is not invalid (U7)
        RuleFor(x => x.Email)
            .NotEmpty()
            .DependentRules(() =>
                RuleFor(x => EmailAddress.From(x.Email).Value)
                    .EmailAddress()
                    .OverridePropertyName(nameof(RegisterUserCommand.Email))
                    .MaximumLength(Constants.MaxEmailLength));

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(Constants.MinPasswordLength);
    }
}
