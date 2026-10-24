using FluentValidation;

namespace DataTrustHub.Features.Administration.ChangeMemberRole;

internal class ChangeMemberRoleValidator : AbstractValidator<ChangeMemberRoleCommand>
{
    public ChangeMemberRoleValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.CallerId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();

        // Case-sensitive, as the role names are stored
        RuleFor(x => x.Role)
            .Must(role => Constants.AssignableRoles.Contains(role))
            .WithMessage(Constants.RoleNotAssignableMessage);
    }
}
