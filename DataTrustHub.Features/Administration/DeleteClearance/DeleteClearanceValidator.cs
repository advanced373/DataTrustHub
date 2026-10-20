using FluentValidation;

namespace DataTrustHub.Features.Administration.DeleteClearance;

internal class DeleteClearanceValidator : AbstractValidator<DeleteClearanceCommand>
{
    public DeleteClearanceValidator()
    {
        RuleFor(x => x.ClearanceId).NotEmpty();
    }
}
