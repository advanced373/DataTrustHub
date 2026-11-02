using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Infrastructure.Persistance.Model
{
    // Invariant C3 in the database: a user holds at most one clearance per policy. No foreign keys to the
    // user or the policy: aggregates reference each other by id
    [Index(nameof(UserId), nameof(PolicyId), IsUnique = true)]
    public class DbClearance: DbEntity
    {
        public required string Name { get; set; }
        public required DbClassificationLevel ClassificationLevel { get; set; }
        public required Guid PolicyId { get; set; }
        public required Guid UserId { get; set; }
    }
}
