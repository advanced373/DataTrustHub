using DataTrustHub.Domain.User;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace DataTrustHub.Infrastructure.Persistance
{
    public class DContext(DbContextOptions<DContext> options, IMediator mediator) : DbContext(options)
    {
        public DbSet<User> Users => Set<User>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
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
