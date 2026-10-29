using System.ComponentModel.DataAnnotations;
using DataTrustHub.Services.Api;

namespace DataTrustHub.Models;

public sealed class UploadDataViewModel
{
    [Required(ErrorMessage = Messages.FileRequired)]
    public IFormFile? File { get; set; }

    // Empty keeps the file personal: only you see it
    [Display(Name = "Policy")]
    public Guid? PolicyId { get; set; }

    [Display(Name = "Classification level")]
    public int? LevelPriority { get; set; }

    // What the user may choose; filled by the controller, not posted back
    public ClassificationOptionsResponse Options { get; set; } = new([], []);
}
