namespace DataTrustHub.Features._Shared.Seeding;

// First admin account, read from configuration (User Secrets locally): AdminSeed:Email, AdminSeed:Password
public class AdminSeedOptions
{
    public const string SectionName = "AdminSeed";
    public string? Email { get; init; }
    public string? Password { get; init; }

    internal bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);
}
