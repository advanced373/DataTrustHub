using FluentValidation;

namespace DataTrustHub.Features.Administration.AppointOrganizationAdmin;

internal class AppointOrganizationAdminValidator : AbstractValidator<AppointOrganizationAdminCommand>
{
    public AppointOrganizationAdminValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.CallerId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}
