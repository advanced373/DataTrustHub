using DataTrustHub.Domain.Clearances;
using DataTrustHub.Infrastructure.Persistance.Mapping;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Infrastructure.Persistance.Repositories;

internal sealed class ClearanceRepository(DContext db) : IClearanceRepository
{
    public async Task<Clearance?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        (await WithLevel().FirstOrDefaultAsync(c => c.Id == id, cancellationToken))?.ToDomain();

    public async Task<IReadOnlyList<Clearance>> ListForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await WithLevel().Where(c => c.UserId == userId).ToListAsync(cancellationToken);
        return rows.Select(row => row.ToDomain()).ToList();
    }

    public Task<bool> ExistsAsync(Guid userId, Guid policyId, CancellationToken cancellationToken) =>
        db.Clearances.AnyAsync(c => c.UserId == userId && c.PolicyId == policyId, cancellationToken);

    public Task<bool> AnyForPolicyAsync(Guid policyId, CancellationToken cancellationToken) =>
        db.Clearances.AnyAsync(c => c.PolicyId == policyId, cancellationToken);

    // Classification levels are rows of their own; one with the same name and priority is reused
    public async Task AddAsync(Clearance clearance, CancellationToken cancellationToken)
    {
        var row = clearance.ToDb();
        var storedLevel = await FindLevelAsync(clearance.Level, cancellationToken);
        if (storedLevel is not null) row.ClassificationLevel = storedLevel;
        db.Clearances.Add(row);
    }

    // The level row stays: other clearances may share it
    public async Task RemoveAsync(Clearance clearance, CancellationToken cancellationToken) =>
        db.Clearances.Remove(await db.Clearances.FirstAsync(c => c.Id == clearance.Id, cancellationToken));

    private IQueryable<DbClearance> WithLevel() =>
        db.Clearances.AsNoTracking().Include(c => c.ClassificationLevel);

    private Task<DbClassificationLevel?> FindLevelAsync(ClassificationLevel level, CancellationToken cancellationToken) =>
        db.ClassificationLevels.FirstOrDefaultAsync(
            l => l.Name == level.Name && l.Priority == level.Priority, cancellationToken);
}
