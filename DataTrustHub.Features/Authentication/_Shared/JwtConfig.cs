namespace DataTrustHub.Features.Authentication._Shared;

public class JwtConfig
{
    public const string SectionName = "Jwt";
    public required string Secret { get; init; }
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public int ExpiryMinutes { get; init; }
}
