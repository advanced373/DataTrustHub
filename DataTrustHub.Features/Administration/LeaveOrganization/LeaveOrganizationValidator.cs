using FluentValidation;

namespace DataTrustHub.Features.Administration.LeaveOrganization;

internal class LeaveOrganizationValidator : AbstractValidator<LeaveOrganizationCommand>
{
    public LeaveOrganizationValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.CallerId).NotEmpty();
    }
}
