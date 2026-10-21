using DataTrustHub.Domain.DataItems;
using DataTrustHub.Infrastructure.Persistance.Mapping;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Infrastructure.Persistance.Repositories;

internal sealed class DataItemRepository(DContext db) : IDataItemRepository
{
    public async Task<DataItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        (await db.DataItems.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken))?.ToDomain();

    public async Task<IReadOnlyList<DataItem>> ListActiveOwnedByAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var rows = await db.DataItems.AsNoTracking()
            .Where(d => d.OwnerUserId == ownerUserId && !d.IsDeleted)
            .ToListAsync(cancellationToken);
        return rows.Select(row => row.ToDomain()).ToList();
    }

    public void Add(DataItem dataItem) => db.DataItems.Add(dataItem.ToDb());

    public async Task UpdateAsync(DataItem dataItem, CancellationToken cancellationToken)
    {
        var row = await db.DataItems.FirstAsync(d => d.Id == dataItem.Id, cancellationToken);
        dataItem.CopyTo(row);
    }
}
