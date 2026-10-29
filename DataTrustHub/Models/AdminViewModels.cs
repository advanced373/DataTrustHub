using System.ComponentModel.DataAnnotations;

namespace DataTrustHub.Models;

// A list page: the items, or the error that prevented loading them
public sealed record ListViewModel<T>(IReadOnlyList<T> Items, string? ErrorMessage = null);

// A row's delete button: the id to delete and the question the browser asks first
public sealed record DeleteButtonModel(Guid Id, string ConfirmMessage);

public sealed class CreateAccountViewModel
{
    [Required, EmailAddress, StringLength(Constants.MaxEmailLength)]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), MinLength(Constants.MinPasswordLength)]
    public string Password { get; set; } = string.Empty;
}

public sealed class CreateOrganizationViewModel
{
    [Required, StringLength(Constants.MaxNameLength)]
    public string Name { get; set; } = string.Empty;
}

// The platform admin appoints an organization's first organization admin by their email address
public sealed class AppointOrganizationAdminViewModel
{
    [Required, EmailAddress, StringLength(Constants.MaxEmailLength)]
    public string Email { get; set; } = string.Empty;

    // Filled by the controller from the route and the API, not posted back
    public Guid OrganizationId { get; set; }

    public string OrganizationName { get; set; } = string.Empty;
}
