using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Infrastructure.Persistance.Model
{
    // Invariant M1 in the database: a user has at most one membership per organization. No foreign
    // keys: aggregates reference each other by id
    [Index(nameof(UserId), nameof(OrganizationId), IsUnique = true)]
    public class DbMembership: DbEntity
    {
        public const int RoleMaxLength = 32;

        public required Guid UserId { get; set; }
        public required Guid OrganizationId { get; set; }

        // The name of a MembershipRole
        [MaxLength(RoleMaxLength)]
        public required string Role { get; set; }
    }
}
