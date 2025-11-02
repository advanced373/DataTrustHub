using FluentValidation;

namespace DataTrustHub.Application.Organization.AssignUser
{
    public class AssignUserToOrganizationCommandValidator : AbstractValidator<AssignUserToOrganizationCommand>
    {
        public AssignUserToOrganizationCommandValidator()
        {
            RuleFor(x => x.UserId).NotEmpty();
            RuleFor(x => x.OrganizationId).NotEmpty();
        }
    }
}
