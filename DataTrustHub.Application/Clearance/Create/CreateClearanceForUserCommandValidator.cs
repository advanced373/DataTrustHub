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
            RuleFor(x => x.ClassificationLevelName).NotEmpty();
            RuleFor(x => x.ClassificationLevelPriority).GreaterThanOrEqualTo(0);
        }
    }
}
