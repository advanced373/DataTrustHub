using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataTrustHub.Infrastructure.Persistance.Configurations
{
    // Invariant D1 in the database: a data item is either personal (no policy, no classification level) or
    // classified (policy and level together). Attributes cannot declare a check constraint, so it lives here;
    // DContext picks this class up through ApplyConfigurationsFromAssembly. Soft-deleted rows follow it too
    public class DbDataItemConfiguration : IEntityTypeConfiguration<DbDataItem>
    {
        public const string ClassificationConstraintName = "CK_DataItems_Classification";

        // Both columns empty (personal) or both set (classified); the bracketed names suit SQL Server and SQLite
        public const string ClassificationConstraintSql =
            "([PolicyId] IS NULL AND [ClassificationPriority] IS NULL)"
            + " OR ([PolicyId] IS NOT NULL AND [ClassificationPriority] IS NOT NULL)";

        public void Configure(EntityTypeBuilder<DbDataItem> builder)
        {
            builder.ToTable(table => table.HasCheckConstraint(ClassificationConstraintName, ClassificationConstraintSql));
        }
    }
}
