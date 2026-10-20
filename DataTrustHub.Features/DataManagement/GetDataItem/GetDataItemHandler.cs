using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.DataManagement.GetDataItem;

public record GetDataItemQuery(Guid DataItemId, Guid RequesterId) : IRequest<Result<GetDataItemResponse>>;

public class GetDataItemHandler(DContext db) : IRequestHandler<GetDataItemQuery, Result<GetDataItemResponse>>
{
    private readonly DContext _db = db;

    // Owner-only: another user's item answers 404, so its existence is not revealed
    public async Task<Result<GetDataItemResponse>> Handle(
        GetDataItemQuery query,
        CancellationToken cancellationToken)
    {
        var item = await _db.DataItems
            .AsNoTracking()
            .Where(d => d.Id == query.DataItemId && d.OwnerUserId == query.RequesterId && !d.IsDeleted)
            .Select(d => new GetDataItemResponse(d.Id, d.Name, d.Size, d.SecurityMarking, d.Content))
            .FirstOrDefaultAsync(cancellationToken);

        return item is null
            ? Result.Failure<GetDataItemResponse>(Errors.DataItemNotFound)
            : Result.Success(item);
    }
}
