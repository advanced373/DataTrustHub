using FluentValidation;

namespace DataTrustHub.Features.Administration.ListPolicies;

internal class ListPoliciesValidator : AbstractValidator<ListPoliciesQuery>
{
    public ListPoliciesValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.CallerId).NotEmpty();
    }
}
