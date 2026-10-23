using FluentValidation;

namespace DataTrustHub.Features.Administration.AddMember;

internal class AddMemberValidator : AbstractValidator<AddMemberCommand>
{
    public AddMemberValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.CallerId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();

        // Case-sensitive, as the role names are stored
        RuleFor(x => x.Role)
            .Must(role => Constants.AddableRoles.Contains(role))
            .WithMessage(Constants.RoleNotAddableMessage);
    }
}
