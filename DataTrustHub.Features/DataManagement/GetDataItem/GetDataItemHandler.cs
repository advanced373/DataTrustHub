using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.DataManagement.GetDataItem;

public record GetDataItemQuery(Guid DataItemId, Guid RequesterId);

public class GetDataItemHandler(DContext db) : IQueryHandler<GetDataItemQuery, Result<GetDataItemResponse>>
{
    private readonly DContext _db = db;

    // An item the requester may not read answers 404, so its existence is not revealed
    public async Task<Result<GetDataItemResponse>> Handle(
        GetDataItemQuery query,
        CancellationToken cancellationToken)
    {
        var item = await (
                from candidate in _db.DataItems.AsNoTracking().VisibleTo(_db, query.RequesterId)
                where candidate.Id == query.DataItemId
                join policy in _db.Policies on candidate.PolicyId equals policy.Id into policies
                from policy in policies.DefaultIfEmpty()
                select new GetDataItemResponse(
                    candidate.Id,
                    candidate.Name,
                    candidate.Size,
                    candidate.SecurityMarking,
                    candidate.Content,
                    candidate.ClassificationPriority,
                    policy == null ? null : policy.Name,
                    candidate.OwnerUserId == query.RequesterId))
            .FirstOrDefaultAsync(cancellationToken);

        return item is null
            ? Result.Failure<GetDataItemResponse>(Errors.DataItemNotFound)
            : Result.Success(item);
    }
}
