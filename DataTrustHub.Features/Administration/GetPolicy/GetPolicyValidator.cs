using FluentValidation;

namespace DataTrustHub.Features.Administration.GetPolicy;

internal class GetPolicyValidator : AbstractValidator<GetPolicyQuery>
{
    public GetPolicyValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.CallerId).NotEmpty();
        RuleFor(x => x.PolicyId).NotEmpty();
    }
}
