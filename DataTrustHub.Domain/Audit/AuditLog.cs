using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.Audit
{
    public sealed class AuditLog : Entity, IAggregateRoot
    {
        public required Guid Id { get; set; }
        public required string Action { get; set; } // "Create" or "Delete"
        public required string EntityType { get; set; } // "User", "Organization", etc.
        public required Guid EntityId { get; set; }
        public DateTime Timestamp { get; set; }
        public string? UserId { get; set; } // Will be null if no authentication
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public string? RequestPath { get; set; }
        public string? RequestMethod { get; set; }
    }
}

