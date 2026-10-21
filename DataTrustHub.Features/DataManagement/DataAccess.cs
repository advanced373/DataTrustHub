using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;

namespace DataTrustHub.Features.DataManagement;

// The read rule for data items, shared by the DataManagement read slices (#5):
// a non-deleted item is visible to its owner, and to anyone holding a clearance on the item's
// policy with a priority at least the item's. Personal items have no policy, so only the owner
// sees them. The Admin role grants nothing here (need-to-know).
public static class DataAccess
{
    public static IQueryable<DbDataItem> VisibleTo(this IQueryable<DbDataItem> items, DContext db, Guid userId) =>
        items.Where(item => !item.IsDeleted
            && (item.OwnerUserId == userId
                || db.Clearances.Any(clearance =>
                    clearance.UserId == userId
                    && clearance.PolicyId == item.PolicyId
                    && clearance.ClassificationLevel.Priority >= item.ClassificationPriority)));
}
