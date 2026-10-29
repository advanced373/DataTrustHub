using FluentValidation;

namespace DataTrustHub.Features.Administration.GetClearance;

internal class GetClearanceValidator : AbstractValidator<GetClearanceQuery>
{
    public GetClearanceValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.CallerId).NotEmpty();
        RuleFor(x => x.ClearanceId).NotEmpty();
    }
}
