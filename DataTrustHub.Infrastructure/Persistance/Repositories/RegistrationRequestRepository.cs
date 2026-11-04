using DataTrustHub.Domain.Registrations;
using DataTrustHub.Domain.Users;
using DataTrustHub.Infrastructure.Persistance.Mapping;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Infrastructure.Persistance.Repositories;

// Reads return detached domain entities; Add and RemoveAsync are tracked here and committed by IUnitOfWork.
// RemoveExpiredAsync is the exception: a set-based delete that runs at once (ADR-0026)
internal sealed class RegistrationRequestRepository(DContext db) : IRegistrationRequestRepository
{
    public async Task<RegistrationRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        (await db.RegistrationRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken))?.ToDomain();

    // Emails are stored normalized (U7), so the normalized argument is compared by equality in the database
    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken)
    {
        var normalizedEmail = EmailAddress.From(email).Value;
        return db.RegistrationRequests.AnyAsync(r => r.Email == normalizedEmail, cancellationToken);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken) =>
        db.RegistrationRequests.CountAsync(cancellationToken);

    public Task<int> RemoveExpiredAsync(DateTime requestedBefore, CancellationToken cancellationToken) =>
        db.RegistrationRequests.Where(r => r.RequestedAt < requestedBefore).ExecuteDeleteAsync(cancellationToken);

    public void Add(RegistrationRequest request) => db.RegistrationRequests.Add(request.ToDb());

    public async Task RemoveAsync(RegistrationRequest request, CancellationToken cancellationToken) =>
        db.RegistrationRequests.Remove(
            await db.RegistrationRequests.FirstAsync(r => r.Id == request.Id, cancellationToken));
}
