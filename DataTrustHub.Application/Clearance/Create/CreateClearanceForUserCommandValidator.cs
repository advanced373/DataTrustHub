using FluentValidation;

namespace DataTrustHub.Application.Clearance.Create
{
    public class CreateClearanceForUserCommandValidator : AbstractValidator<CreateClearanceForUserCommand>
    {
        public CreateClearanceForUserCommandValidator()
        {
            RuleFor(x => x.UserId).NotEmpty();
            RuleFor(x => x.PolicyId).NotEmpty();
            RuleFor(x => x.Name).NotEmpty();
        }
    }
}
