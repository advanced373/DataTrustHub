namespace DataTrustHub.Infrastructure.Persistance.Model
{
    public class DbDataItem: DbEntity
    {
        public required string Name { get; set; }
        public required long Size { get; set; }
        public string? Content { get; set; }
        public required Guid OwnerUserId { get; set; }
        public required string SecurityMarking { get; set; }
    }
}
