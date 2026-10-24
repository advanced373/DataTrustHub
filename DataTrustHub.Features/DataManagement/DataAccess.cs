using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;

namespace DataTrustHub.Features.DataManagement;

// The read rule for data items, shared by the DataManagement read slices (#5), in its SQL form
// (ADR-0014, D3'): a non-deleted personal item (no policy) is visible to its owner only; a
// non-deleted classified item is visible only to holders of a clearance on the item's policy with a
// priority at least the item's, the owner included. Personal items match no clearance, because their
// policy is NULL. The Admin role grants nothing here (need-to-know).
public static class DataAccess
{
    public static IQueryable<DbDataItem> VisibleTo(this IQueryable<DbDataItem> items, DContext db, Guid userId) =>
        items.Where(item => !item.IsDeleted
            && ((item.PolicyId == null && item.OwnerUserId == userId)
                || db.Clearances.Any(clearance =>
                    clearance.UserId == userId
                    && clearance.PolicyId == item.PolicyId
                    && clearance.ClassificationLevel.Priority >= item.ClassificationPriority)));
}
