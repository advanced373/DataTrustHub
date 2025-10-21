using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Application.Abstractions.Data
{
    public interface IApplicationDbContext
    {
        /// <summary>To do items.</summary>
        DbSet<User> Users { get; }

        /// <summary>Users.</summary>
        DbSet<Organization> Organizations { get; }
        DbSet<Policy> Policies { get; }
        DbSet<Clearance> Clearances { get; }
        DbSet<Data> Data { get; }

        /// <summary>Save changes.</summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="int" /> of how many items have been saved.</returns>
        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}