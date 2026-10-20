using FluentValidation;

namespace DataTrustHub.Features.Administration.CreateClearance;

internal class CreateClearanceValidator : AbstractValidator<CreateClearanceCommand>
{
    public CreateClearanceValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.PolicyId).NotEmpty();

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(Constants.MaxNameLength);

        RuleFor(x => x.ClassificationLevelName)
            .NotEmpty()
            .MaximumLength(Constants.MaxNameLength);

        RuleFor(x => x.ClassificationLevelPriority)
            .GreaterThanOrEqualTo(Constants.MinPriority);
    }
}
