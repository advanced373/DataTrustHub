using FluentValidation;

namespace DataTrustHub.Features.Administration.GetClearance;

internal class GetClearanceValidator : AbstractValidator<GetClearanceQuery>
{
    public GetClearanceValidator()
    {
        RuleFor(x => x.ClearanceId).NotEmpty();
    }
}
