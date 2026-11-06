using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Infrastructure.Persistance.Model
{
    // Invariant O1 in the database: organization names are unique
    [Index(nameof(Name), IsUnique = true)]
    public class DbOrganization: DbEntity
    {
        // The validators of CreateOrganization and RenameOrganization cap a name at this length
        public const int NameMaxLength = 256;

        [MaxLength(NameMaxLength)]
        public required string Name { get; set; }
    }
}
