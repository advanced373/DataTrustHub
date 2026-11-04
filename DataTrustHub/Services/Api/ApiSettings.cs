using System.ComponentModel.DataAnnotations;

namespace DataTrustHub.Services.Api;

public sealed class ApiSettings
{
    [Required, Url]
    public string BaseUrl { get; set; } = string.Empty;

    // The key shared with the API (its Registration:TrustedClientKey), sent with the browser's address on
    // register only (TrustedClientHeaders). A secret: set in User Secrets or the environment (Api__ClientKey),
    // never in appsettings.json. Empty or missing: neither header is sent
    public string? ClientKey { get; set; }
}
