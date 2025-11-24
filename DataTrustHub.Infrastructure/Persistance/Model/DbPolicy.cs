
namespace DataTrustHub.Infrastructure.Persistance.Model
{
    public class DbPolicy: DbEntity
    {
        public required string Name { get; set; }
        public IReadOnlyCollection<DbClassificationLevel> ClassificationLevels { get; set; } = null!;
        public required Guid OrganizationId { get; set; }
    }
}
