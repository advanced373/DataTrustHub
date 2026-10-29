namespace DataTrustHub.Domain.Users;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    // Normalizes the email as EmailAddress does (U7) before the lookup, so case and surrounding spaces do not matter
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    // Normalizes the email as EmailAddress does (U7) before the check, so case and surrounding spaces do not matter
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);

    void Add(User user);

    Task UpdateAsync(User user, CancellationToken cancellationToken);

    Task RemoveAsync(User user, CancellationToken cancellationToken);
}
