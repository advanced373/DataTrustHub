using DataTrustHub.Domain.Users;

namespace DataTrustHub.Domain.Registrations;

// A sign-up waiting for a platform admin's approval (ADR-0026). It is not an account: the user is created
// only when the request is approved, so Users holds approved accounts only. It holds the normalized email
// (U7) and the password hash, never the plain password
public sealed class RegistrationRequest
{
    private const string RequestedAtNotUtc = "A registration request's time is in UTC.";

    // Rebuilds a stored entity; used by the persistence mappers only (internal, visible to Infrastructure)
    internal RegistrationRequest(Guid id, string email, string hashedPassword, DateTime requestedAt)
    {
        Id = id;
        Email = email;
        HashedPassword = hashedPassword;
        RequestedAt = requestedAt;
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; }

    public string HashedPassword { get; private set; }

    // UTC
    public DateTime RequestedAt { get; private set; }

    // The time comes from code, never from user input, so a local time is a programming error and throws
    public static RegistrationRequest Submit(string email, string hashedPassword, DateTime requestedAt)
    {
        if (requestedAt.Kind != DateTimeKind.Utc) throw new ArgumentException(RequestedAtNotUtc, nameof(requestedAt));

        return new RegistrationRequest(Guid.NewGuid(), EmailAddress.From(email).Value, hashedPassword, requestedAt);
    }

    // R2: a request older than the pending lifetime has expired; it is purged and no longer counts. The purge
    // (IRegistrationRequestRepository.RemoveExpiredAsync) applies the same strict comparison in the database
    public bool IsExpired(DateTime now, TimeSpan lifetime) => RequestedAt < now - lifetime;
}
