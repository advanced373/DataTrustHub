using DataTrustHub.Domain.ReadModels.DataItems;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Infrastructure.Persistance.ReadModels;

// Projects straight to the read models in SQL, untracked; the read and delete rules are the SQL forms in
// DataAccess, so no item is filtered on the client
internal sealed class DataItemQueries(DContext db) : IDataItemQueries
{
    public async Task<IReadOnlyList<DataItemListEntry>> ListReadableAsync(
        Guid requesterId,
        CancellationToken cancellationToken)
    {
        var deletable = db.DataItems.DeletableBy(db, requesterId);
        return await (
                from item in db.DataItems.AsNoTracking().VisibleTo(db, requesterId)
                join owner in db.Users on item.OwnerUserId equals owner.Id into owners
                from owner in owners.DefaultIfEmpty()
                join policy in db.Policies on item.PolicyId equals policy.Id into policies
                from policy in policies.DefaultIfEmpty()
                orderby item.Name
                select new DataItemListEntry(
                    item.Id,
                    item.Name,
                    item.Size,
                    item.SecurityMarking,
                    item.ClassificationPriority,
                    item.PolicyId,
                    policy == null ? null : policy.Name,
                    owner == null ? null : owner.Email,
                    item.OwnerUserId == requesterId,
                    deletable.Any(candidate => candidate.Id == item.Id)))
            .ToListAsync(cancellationToken);
    }

    public Task<ReadableDataItem?> FindReadableAsync(
        Guid dataItemId,
        Guid requesterId,
        CancellationToken cancellationToken)
    {
        var deletable = db.DataItems.DeletableBy(db, requesterId);
        return (
                from candidate in db.DataItems.AsNoTracking().VisibleTo(db, requesterId)
                where candidate.Id == dataItemId
                join policy in db.Policies on candidate.PolicyId equals policy.Id into policies
                from policy in policies.DefaultIfEmpty()
                select new ReadableDataItem(
                    candidate.Id,
                    candidate.Name,
                    candidate.Size,
                    candidate.SecurityMarking,
                    candidate.Content,
                    candidate.ClassificationPriority,
                    policy == null ? null : policy.Name,
                    policy == null ? null : policy.OrganizationId,
                    candidate.OwnerUserId == requesterId,
                    deletable.Any(other => other.Id == candidate.Id)))
            .FirstOrDefaultAsync(cancellationToken);
    }

    // No read rule here: any row with this id, deleted or not; the organization only while the policy exists
    public Task<DeniedReadTarget?> FindDeniedReadTargetAsync(Guid dataItemId, CancellationToken cancellationToken) =>
        db.DataItems.AsNoTracking()
            .Where(item => item.Id == dataItemId)
            .Select(item => new DeniedReadTarget(db.Policies
                .Where(policy => policy.Id == item.PolicyId)
                .Select(policy => (Guid?)policy.OrganizationId)
                .FirstOrDefault()))
            .FirstOrDefaultAsync(cancellationToken);
}
