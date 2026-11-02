using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Infrastructure.Persistance.Model
{
    // Invariant P2 in the database: policy names are unique within an organization. No foreign key to
    // the organization: aggregates reference each other by id
    [Index(nameof(OrganizationId), nameof(Name), IsUnique = true)]
    public class DbPolicy: DbEntity
    {
        // The validator of CreatePolicy caps a name at this length
        public const int NameMaxLength = 256;

        [MaxLength(NameMaxLength)]
        public required string Name { get; set; }
        public IReadOnlyCollection<DbClassificationLevel> ClassificationLevels { get; set; } = null!;
        public required Guid OrganizationId { get; set; }
    }
}
