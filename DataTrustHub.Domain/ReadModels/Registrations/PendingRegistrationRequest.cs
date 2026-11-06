namespace DataTrustHub.Domain.ReadModels.Registrations;

// A registration request waiting for the platform admin. RequestedAt is as the database reads it back,
// without a kind; it was written in UTC
public sealed record PendingRegistrationRequest(Guid Id, string Email, DateTime RequestedAt);
