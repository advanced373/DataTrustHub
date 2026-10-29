using System.ComponentModel.DataAnnotations;

namespace DataTrustHub.Services.Api;

public sealed class ApiSettings
{
    [Required, Url]
    public string BaseUrl { get; set; } = string.Empty;
}
