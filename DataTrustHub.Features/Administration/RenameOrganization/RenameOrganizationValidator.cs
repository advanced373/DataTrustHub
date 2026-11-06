using FluentValidation;

namespace DataTrustHub.Features.Administration.RenameOrganization;

internal class RenameOrganizationValidator : AbstractValidator<RenameOrganizationCommand>
{
    public RenameOrganizationValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(Constants.MaxNameLength);
    }
}
