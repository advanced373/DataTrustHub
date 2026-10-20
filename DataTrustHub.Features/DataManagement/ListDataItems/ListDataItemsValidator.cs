using FluentValidation;

namespace DataTrustHub.Features.DataManagement.ListDataItems;

internal class ListDataItemsValidator : AbstractValidator<ListDataItemsQuery>
{
    public ListDataItemsValidator()
    {
        RuleFor(x => x.OwnerId).NotEmpty();
    }
}
