using DataTrustHub.Domain.Shares;
using DataTrustHub.Infrastructure.Persistance.Mapping;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Infrastructure.Persistance.Repositories;

// Reads return detached domain entities; writes are tracked here and committed by IUnitOfWork
internal sealed class ShareRepository(DContext db) : IShareRepository
{
    public async Task<Share?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        (await db.Shares.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken))?.ToDomain();

    // The same comparison as Share.IsActiveAt (S5), translated to SQL
    public Task<bool> HasActiveAsync(
        Guid dataItemId, Guid recipientUserId, DateTime now, CancellationToken cancellationToken) =>
        db.Shares.AnyAsync(
            s => s.DataItemId == dataItemId
                && s.RecipientUserId == recipientUserId
                && s.RevokedAt == null
                && (s.ExpiresAt == null || s.ExpiresAt > now),
            cancellationToken);

    public void Add(Share share) => db.Shares.Add(share.ToDb());

    public async Task UpdateAsync(Share share, CancellationToken cancellationToken)
    {
        var row = await db.Shares.FirstAsync(s => s.Id == share.Id, cancellationToken);
        share.CopyTo(row);
    }

    // Each share not revoked yet is revoked through the entity, so Share.Revoke stays the only place that sets it
    public async Task RevokeAllForDataItemAsync(Guid dataItemId, DateTime revokedAt, CancellationToken cancellationToken)
    {
        var rows = await db.Shares
            .Where(s => s.DataItemId == dataItemId && s.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            var share = row.ToDomain();
            if (share.Revoke(revokedAt).IsSuccess) share.CopyTo(row);
        }
    }

    public async Task RemoveAllForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await db.Shares
            .Where(s => s.RecipientUserId == userId || s.SharedByUserId == userId)
            .ToListAsync(cancellationToken);
        db.Shares.RemoveRange(rows);
    }
}
