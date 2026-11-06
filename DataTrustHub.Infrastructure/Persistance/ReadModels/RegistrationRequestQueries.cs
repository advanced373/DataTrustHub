using DataTrustHub.Domain.ReadModels.Registrations;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Infrastructure.Persistance.ReadModels;

// Projects straight to the read model in SQL, untracked; the password hash is never selected
internal sealed class RegistrationRequestQueries(DContext db) : IRegistrationRequestQueries
{
    public async Task<IReadOnlyList<PendingRegistrationRequest>> ListPendingAsync(
        CancellationToken cancellationToken) =>
        await db.RegistrationRequests
            .AsNoTracking()
            // Oldest first; the id breaks ties so the order is stable
            .OrderBy(request => request.RequestedAt)
            .ThenBy(request => request.Id)
            .Select(request => new PendingRegistrationRequest(request.Id, request.Email, request.RequestedAt))
            .ToListAsync(cancellationToken);
}
