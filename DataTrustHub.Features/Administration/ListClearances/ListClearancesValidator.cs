using FluentValidation;

namespace DataTrustHub.Features.Administration.ListClearances;

internal class ListClearancesValidator : AbstractValidator<ListClearancesQuery>
{
    public ListClearancesValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.CallerId).NotEmpty();
    }
}
