using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace DataTrustHub.Infrastructure.Persistance
{
    // Also the unit of work: the repositories of a request share this scoped context (ADR-0012)
    public class DContext(DbContextOptions<DContext> options) : DbContext(options), IUnitOfWork
    {
        // SQL Server error numbers: a duplicate key in a unique index, and a broken unique constraint
        private const int DuplicateKeyInUniqueIndex = 2601;
        private const int UniqueConstraintViolated = 2627;

        public DbSet<DbUser> Users => Set<DbUser>();
        public DbSet<DbOrganization> Organizations => Set<DbOrganization>();
        public DbSet<DbClassificationLevel> ClassificationLevels => Set<DbClassificationLevel>();
        public DbSet<DbDataItem> DataItems => Set<DbDataItem>();
        public DbSet<DbClearance> Clearances => Set<DbClearance>();
        public DbSet<DbPolicy> Policies => Set<DbPolicy>();
        public DbSet<DbMembership> Memberships => Set<DbMembership>();
        public DbSet<DbAuditRecord> AuditRecords => Set<DbAuditRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            //modelBuilder.Entity<DbClassificationLevel>(entity =>
            //{
            //    entity.HasNoKey();
            //});
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }

        // A unique-index violation becomes the domain's UniqueConstraintViolationException, so use cases
        // can catch it without depending on EF Core or SQL Server (M1 under concurrency)
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await base.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
            {
                throw new UniqueConstraintViolationException(exception);
            }
        }

        private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
            exception.InnerException is SqlException sqlException
            && sqlException.Number is DuplicateKeyInUniqueIndex or UniqueConstraintViolated;
    }
}
