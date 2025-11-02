using FluentValidation;

namespace DataTrustHub.Application.Policy.Create
{
    public class CreatePolicyCommandValidator : AbstractValidator<CreatePolicyCommand>
    {
        public CreatePolicyCommandValidator()
        {
            RuleFor(x => x.Name).NotEmpty();
            RuleFor(x => x.OrganizationId).NotEmpty();
        }
    }
}
