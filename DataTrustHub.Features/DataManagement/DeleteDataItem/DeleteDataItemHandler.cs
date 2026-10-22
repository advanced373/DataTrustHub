using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.DataItems;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.DataManagement.DeleteDataItem;

public record DeleteDataItemCommand(Guid DataItemId, Guid RequesterId);

public class DeleteDataItemHandler(IDataItemRepository dataItems, IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteDataItemCommand, Result<Guid>>
{
    private readonly IDataItemRepository _dataItems = dataItems;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    public async Task<Result<Guid>> Handle(
        DeleteDataItemCommand command,
        CancellationToken cancellationToken)
    {
        var dataItem = await _dataItems.GetByIdAsync(command.DataItemId, cancellationToken);
        if (dataItem is null)
            return Result.Failure<Guid>(DataItemErrors.NotFound);

        // The entity hides other users' and already deleted items behind "not found"
        var deleted = dataItem.MarkDeleted(command.RequesterId);
        if (deleted.IsFailure)
            return Result.Failure<Guid>(deleted.Error);

        await _dataItems.UpdateAsync(dataItem, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(command.DataItemId);
    }
}
