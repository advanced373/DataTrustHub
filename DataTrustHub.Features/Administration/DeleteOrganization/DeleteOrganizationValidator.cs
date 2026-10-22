using FluentValidation;

namespace DataTrustHub.Features.Administration.DeleteOrganization;

internal class DeleteOrganizationValidator : AbstractValidator<DeleteOrganizationCommand>
{
    public DeleteOrganizationValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
    }
}
