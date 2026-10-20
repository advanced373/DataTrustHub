using FluentValidation;

namespace DataTrustHub.Features.DataManagement.GetDataItem;

internal class GetDataItemValidator : AbstractValidator<GetDataItemQuery>
{
    public GetDataItemValidator()
    {
        RuleFor(x => x.DataItemId).NotEmpty();
        RuleFor(x => x.RequesterId).NotEmpty();
    }
}
