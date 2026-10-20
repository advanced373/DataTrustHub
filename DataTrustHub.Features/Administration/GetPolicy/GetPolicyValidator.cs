using FluentValidation;

namespace DataTrustHub.Features.Administration.GetPolicy;

internal class GetPolicyValidator : AbstractValidator<GetPolicyQuery>
{
    public GetPolicyValidator()
    {
        RuleFor(x => x.PolicyId).NotEmpty();
    }
}
