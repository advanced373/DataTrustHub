using FluentValidation;

namespace DataTrustHub.Features.Administration.ListMembers;

internal class ListMembersValidator : AbstractValidator<ListMembersQuery>
{
    public ListMembersValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.CallerId).NotEmpty();
    }
}
