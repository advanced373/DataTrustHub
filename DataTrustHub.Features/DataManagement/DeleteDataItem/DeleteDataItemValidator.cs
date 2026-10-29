using FluentValidation;

namespace DataTrustHub.Features.DataManagement.DeleteDataItem;

internal class DeleteDataItemValidator : AbstractValidator<DeleteDataItemCommand>
{
    public DeleteDataItemValidator()
    {
        RuleFor(x => x.DataItemId).NotEmpty();
        RuleFor(x => x.RequesterId).NotEmpty();
    }
}
