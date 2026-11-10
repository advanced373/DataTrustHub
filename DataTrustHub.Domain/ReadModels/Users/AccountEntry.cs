namespace DataTrustHub.Domain.ReadModels.Users;

// A user's account as the platform admin sees it in the list and on its own page: its email and platform role
public sealed record AccountEntry(Guid Id, string Email, string Role);
