using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.DataManagement.DeleteDataItem;

public record DeleteDataItemCommand(Guid DataItemId, Guid RequesterId) : IRequest<Result<Guid>>;

public class DeleteDataItemHandler(DContext db) : IRequestHandler<DeleteDataItemCommand, Result<Guid>>
{
    private readonly DContext _db = db;

    public async Task<Result<Guid>> Handle(
        DeleteDataItemCommand command,
        CancellationToken cancellationToken)
    {
        var dataItem = await _db.DataItems.FirstOrDefaultAsync(
            d => d.Id == command.DataItemId && !d.IsDeleted,
            cancellationToken);

        if (dataItem is null || dataItem.OwnerUserId != command.RequesterId)
            return Result.Failure<Guid>(Errors.DataItemNotFound);

        dataItem.IsDeleted = true;
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(command.DataItemId);
    }
}
