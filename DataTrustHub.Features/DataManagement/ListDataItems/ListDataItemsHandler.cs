using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.DataManagement.ListDataItems;

public record ListDataItemsQuery(Guid RequesterId) : IRequest<Result<IReadOnlyList<DataItemSummary>>>;

public class ListDataItemsHandler(DContext db)
    : IRequestHandler<ListDataItemsQuery, Result<IReadOnlyList<DataItemSummary>>>
{
    private readonly DContext _db = db;

    // Own items plus those the requester's clearances allow; file content is left out, GET /data/{id} returns it
    public async Task<Result<IReadOnlyList<DataItemSummary>>> Handle(
        ListDataItemsQuery query,
        CancellationToken cancellationToken)
    {
        var items = await (
                from item in _db.DataItems.AsNoTracking().VisibleTo(_db, query.RequesterId)
                join owner in _db.Users on item.OwnerUserId equals owner.Id into owners
                from owner in owners.DefaultIfEmpty()
                join policy in _db.Policies on item.PolicyId equals policy.Id into policies
                from policy in policies.DefaultIfEmpty()
                orderby item.Name
                select new DataItemSummary(
                    item.Id,
                    item.Name,
                    item.Size,
                    item.SecurityMarking,
                    item.ClassificationPriority,
                    item.PolicyId,
                    policy == null ? null : policy.Name,
                    owner == null ? null : owner.Email,
                    item.OwnerUserId == query.RequesterId))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<DataItemSummary>>(items);
    }
}
