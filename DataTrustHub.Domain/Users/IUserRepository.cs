namespace DataTrustHub.Domain.Users;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);

    void Add(User user);

    Task UpdateAsync(User user, CancellationToken cancellationToken);

    Task RemoveAsync(User user, CancellationToken cancellationToken);
}
