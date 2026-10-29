namespace DataTrustHub.Infrastructure.Persistance.Model
{
    public class DbDataItem: DbEntity
    {
        public required string Name { get; set; }
        public required long Size { get; set; }
        public string? Content { get; set; }
        public required Guid OwnerUserId { get; set; }
        public required string SecurityMarking { get; set; }
        public bool IsDeleted { get; set; } = false;

        // The policy a classified item belongs to; null for a personal item, which only its owner sees
        public Guid? PolicyId { get; set; }

        // Priority of the item's classification level; readers need a clearance on PolicyId at least this high
        public int? ClassificationPriority { get; set; }
    }
}
