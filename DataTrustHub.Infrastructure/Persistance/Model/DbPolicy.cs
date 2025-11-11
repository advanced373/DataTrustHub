using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Infrastructure.Persistance.Model
{
    public class DbPolicy: DbEntity
    {
        public required string Name { get; set; }
        public DbSet<DbClassificationLevel> ClassificationLevels { get; set; } = null!;
        public required Guid OrganizationId { get; set; }
    }
}
