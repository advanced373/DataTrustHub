using FluentValidation;

namespace DataTrustHub.Features.Administration.RenamePolicy;

internal class RenamePolicyValidator : AbstractValidator<RenamePolicyCommand>
{
    public RenamePolicyValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.CallerId).NotEmpty();
        RuleFor(x => x.PolicyId).NotEmpty();

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(Constants.MaxNameLength);
    }
}
