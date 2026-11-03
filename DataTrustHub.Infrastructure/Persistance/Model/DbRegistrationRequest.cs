using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Infrastructure.Persistance.Model
{
    // Sign-ups waiting for approval (ADR-0026). Invariant R1 in the database: one pending request per email,
    // stored normalized (U7) like DbUser.Email. RequestedAt is indexed for the purge of expired requests (R2)
    [Index(nameof(Email), IsUnique = true)]
    [Index(nameof(RequestedAt))]
    public class DbRegistrationRequest: DbEntity
    {
        // The register validator caps an email at this length, as for DbUser
        public const int EmailMaxLength = 256;

        [MaxLength(EmailMaxLength)]
        public required string Email { get; set; }
        public required string HashedPassword { get; set; }
        public required DateTime RequestedAt { get; set; }
    }
}
