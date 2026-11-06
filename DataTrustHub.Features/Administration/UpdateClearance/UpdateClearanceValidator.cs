using FluentValidation;

namespace DataTrustHub.Features.Administration.UpdateClearance;

internal class UpdateClearanceValidator : AbstractValidator<UpdateClearanceCommand>
{
    public UpdateClearanceValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.CallerId).NotEmpty();
        RuleFor(x => x.ClearanceId).NotEmpty();

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(Constants.MaxNameLength);

        RuleFor(x => x.ClassificationLevelName)
            .NotEmpty()
            .MaximumLength(Constants.MaxNameLength);

        // Invariant C4
        RuleFor(x => x.ClassificationLevelPriority)
            .GreaterThanOrEqualTo(Constants.MinPriority);
    }
}
