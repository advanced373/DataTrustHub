using DataTrustHub.Domain.Users;
using FluentValidation;

namespace DataTrustHub.Features.Authentication.LoginUser;

internal class LoginUserValidator : AbstractValidator<LoginUserCommand>
{
    public LoginUserValidator()
    {
        // The format applies to the normalized address, so surrounding whitespace alone is not invalid (U7)
        RuleFor(x => x.Email)
            .NotEmpty()
            .DependentRules(() =>
                RuleFor(x => EmailAddress.From(x.Email).Value)
                    .EmailAddress()
                    .OverridePropertyName(nameof(LoginUserCommand.Email)));

        RuleFor(x => x.Password).NotEmpty();
    }
}
