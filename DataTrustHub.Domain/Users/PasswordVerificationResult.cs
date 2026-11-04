namespace DataTrustHub.Domain.Users;

// The outcome of checking a password against a stored password hash (IPasswordHasher). The Domain's own
// type, so the Domain does not depend on the hashing library
public enum PasswordVerificationResult
{
    // The password does not match, or the stored hash is unreadable
    Failed,

    // The password matches and the stored hash is current
    Success,

    // The password matches, but the stored hash is in an older format or weaker than configured: hash the
    // password again and replace the stored hash
    SuccessRehashNeeded
}
