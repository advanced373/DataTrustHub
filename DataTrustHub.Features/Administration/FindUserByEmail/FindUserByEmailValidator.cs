using FluentValidation;

namespace DataTrustHub.Features.Administration.FindUserByEmail;

internal class FindUserByEmailValidator : AbstractValidator<FindUserByEmailQuery>
{
    public FindUserByEmailValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.CallerId).NotEmpty();

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(Constants.MaxEmailLength);
    }
}
