using FluentValidation;

namespace DataTrustHub.Application.Organization.Create
{
    public class CreateOrganizationCommandValidator : AbstractValidator<CreateOrganizationCommand>
    {
        public CreateOrganizationCommandValidator()
        {
            RuleFor(x => x.Name).NotEmpty();
        }
    }
}
