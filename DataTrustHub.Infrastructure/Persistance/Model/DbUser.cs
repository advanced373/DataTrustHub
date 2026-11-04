using System.ComponentModel.DataAnnotations;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Infrastructure.Persistance.Model
{
    // Invariant U1 in the database: an email belongs to at most one user. Emails are stored normalized
    // (U7), so the index compares the same form as EmailExistsAsync
    [Index(nameof(Email), IsUnique = true)]
    public class DbUser: DbEntity
    {
        // The validators of the use cases that write an email cap it at this length
        public const int EmailMaxLength = 256;

        [MaxLength(EmailMaxLength)]
        public required string Email { get; set; }
        public required string HashedPassword { get; set; }
        public string Role { get; set; } = UserRoles.User;
    }
}
