using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.DataManagement.ListDataItems;

public record ListDataItemsQuery(Guid OwnerId) : IRequest<Result<IReadOnlyList<DataItemSummary>>>;

public class ListDataItemsHandler(DContext db)
    : IRequestHandler<ListDataItemsQuery, Result<IReadOnlyList<DataItemSummary>>>
{
    private readonly DContext _db = db;

    // Lists the requester's own items; file content is left out, GET /data/{id} returns it
    public async Task<Result<IReadOnlyList<DataItemSummary>>> Handle(
        ListDataItemsQuery query,
        CancellationToken cancellationToken)
    {
        var items = await _db.DataItems
            .AsNoTracking()
            .Where(d => d.OwnerUserId == query.OwnerId && !d.IsDeleted)
            .OrderBy(d => d.Name)
            .Select(d => new DataItemSummary(d.Id, d.Name, d.Size, d.SecurityMarking))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<DataItemSummary>>(items);
    }
}
