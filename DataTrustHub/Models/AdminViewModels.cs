using System.ComponentModel.DataAnnotations;

namespace DataTrustHub.Models;

public sealed class CreateAccountViewModel
{
    [Required, EmailAddress, StringLength(Constants.MaxEmailLength)]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), MinLength(Constants.MinPasswordLength)]
    public string Password { get; set; } = string.Empty;
}

public sealed class CreatePolicyViewModel
{
    [Required, StringLength(Constants.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [Required, Display(Name = "Organization id")]
    public Guid? OrganizationId { get; set; }
}

public sealed class CreateGroupViewModel
{
    [Required, StringLength(Constants.MaxNameLength)]
    public string Name { get; set; } = string.Empty;
}
