using DataTrustHub.Domain.Users;
using FluentValidation;

namespace DataTrustHub.Features.Administration.FindUserByEmail;

internal class FindUserByEmailValidator : AbstractValidator<FindUserByEmailQuery>
{
    public FindUserByEmailValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.CallerId).NotEmpty();

        // Format and length apply to the normalized address, so surrounding whitespace alone is not invalid (U7)
        RuleFor(x => x.Email)
            .NotEmpty()
            .DependentRules(() =>
                RuleFor(x => EmailAddress.From(x.Email).Value)
                    .EmailAddress()
                    .OverridePropertyName(nameof(FindUserByEmailQuery.Email))
                    .MaximumLength(Constants.MaxEmailLength));
    }
}
