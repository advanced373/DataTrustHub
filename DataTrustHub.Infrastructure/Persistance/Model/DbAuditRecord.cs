using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Infrastructure.Persistance.Model
{
    // The audit trail, append-only (ADR-0016). The indexes serve the trail by date, overall and per
    // organization. No foreign keys: a record outlives the users, organizations and items it names
    [Index(nameof(OccurredAt))]
    [Index(nameof(OrganizationId), nameof(OccurredAt))]
    public class DbAuditRecord: DbEntity
    {
        public const int ActionMaxLength = 128;
        public const int TargetTypeMaxLength = 64;
        public const int OutcomeMaxLength = 16;
        public const int ErrorCodeMaxLength = 256;

        public required DateTime OccurredAt { get; set; }
        public Guid? ActorUserId { get; set; }

        [MaxLength(ActionMaxLength)]
        public required string Action { get; set; }

        [MaxLength(TargetTypeMaxLength)]
        public string? TargetType { get; set; }
        public Guid? TargetId { get; set; }
        public Guid? OrganizationId { get; set; }

        // The name of an AuditOutcome
        [MaxLength(OutcomeMaxLength)]
        public required string Outcome { get; set; }

        [MaxLength(ErrorCodeMaxLength)]
        public string? ErrorCode { get; set; }
    }
}
