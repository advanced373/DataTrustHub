using FluentValidation;

namespace DataTrustHub.Features.Administration.ListMyOrganizations;

internal class ListMyOrganizationsValidator : AbstractValidator<ListMyOrganizationsQuery>
{
    public ListMyOrganizationsValidator()
    {
        RuleFor(x => x.CallerId).NotEmpty();
    }
}
