using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataTrustHub.Infrastructure.Persistance.Configurations
{
    // A share belongs to an existing data item. Data items are only soft-deleted, which will revoke their
    // shares (planned, #3); the cascade covers a row removed by hand, since a share without its item means
    // nothing. No navigation: aggregates reference each other by id. DContext picks this class up through
    // ApplyConfigurationsFromAssembly
    public class DbShareConfiguration : IEntityTypeConfiguration<DbShare>
    {
        public void Configure(EntityTypeBuilder<DbShare> builder)
        {
            builder.HasOne<DbDataItem>()
                .WithMany()
                .HasForeignKey(share => share.DataItemId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
