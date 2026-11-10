namespace DataTrustHub.Domain.ReadModels.Registrations;

// The read side of registration requests (ADR-0012 step 4, ADR-0026): every method projects straight to a
// read model in SQL, and none of them carries the password hash. It does not purge: the caller removes the
// expired requests first (R2)
public interface IRegistrationRequestQueries
{
    // The stored requests, oldest first, then by id so the order is stable
    Task<IReadOnlyList<PendingRegistrationRequest>> ListPendingAsync(CancellationToken cancellationToken);
}
