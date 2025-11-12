using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace DataTrustHub.Infrastructure.Persistance
{
    public class DContext(DbContextOptions<DContext> options) : DbContext(options)
    {
        public DbSet<DbUser> Users => Set<DbUser>();
        public DbSet<DbOrganization> Organizations => Set<DbOrganization>();
        public DbSet<DbClassificationLevel> ClassificationLevels => Set<DbClassificationLevel>();
        public DbSet<DbDataItem> DataItems => Set<DbDataItem>();
        public DbSet<DbClearance> Clearances => Set<DbClearance>();
        public DbSet<DbPolicy> Policies => Set<DbPolicy>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            //modelBuilder.Entity<DbClassificationLevel>(entity =>
            //{
            //    entity.HasNoKey();
            //});
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            int result = await base.SaveChangesAsync(cancellationToken);
            return result;
        }
    }
}
