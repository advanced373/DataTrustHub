namespace DataTrustHub.Domain.ReadModels.Users;

// The user an organization admin found by their exact email address (ADR-0014)
public sealed record UserByEmail(Guid Id, string Email);
