using FluentValidation;

namespace DataTrustHub.Features.Administration.CreatePolicy;

internal class CreatePolicyValidator : AbstractValidator<CreatePolicyCommand>
{
    public CreatePolicyValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(Constants.MaxNameLength);

        RuleFor(x => x.OrganizationId)
            .NotEmpty();
    }
}
