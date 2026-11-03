namespace DataTrustHub.Domain.Registrations;

public interface IRegistrationRequestRepository
{
    Task<RegistrationRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    // Normalizes the email as EmailAddress does (U7) before the check, so case and surrounding spaces do not matter
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);

    // The number of stored requests; after RemoveExpiredAsync these are the pending ones
    Task<int> CountAsync(CancellationToken cancellationToken);

    // Deletes every request made before the given UTC time and returns how many were deleted. It runs at
    // once, in its own statement: it is not part of the unit of work and IUnitOfWork.SaveChangesAsync does
    // not commit or roll it back
    Task<int> RemoveExpiredAsync(DateTime requestedBefore, CancellationToken cancellationToken);

    void Add(RegistrationRequest request);

    Task RemoveAsync(RegistrationRequest request, CancellationToken cancellationToken);
}
