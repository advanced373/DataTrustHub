using FluentValidation;

namespace DataTrustHub.Features.Administration.DeleteClearance;

internal class DeleteClearanceValidator : AbstractValidator<DeleteClearanceCommand>
{
    public DeleteClearanceValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.CallerId).NotEmpty();
        RuleFor(x => x.ClearanceId).NotEmpty();
    }
}
