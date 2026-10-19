using System.ComponentModel.DataAnnotations;

namespace DataTrustHub.Models;

public sealed class UploadDataViewModel
{
    [Required(ErrorMessage = Messages.FileRequired)]
    public IFormFile? File { get; set; }
}
