namespace DataTrustHub.Infrastructure.Persistance.Model
{
    public class DbAudit : DbEntity
    {
        public required string Action { get; set; }
        public required string EntityType { get; set; }
        public required Guid EntityId { get; set; }
        public DateTime Timestamp { get; set; }
        public string? UserId { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public string? RequestPath { get; set; }
        public string? RequestMethod { get; set; }
    }
}

