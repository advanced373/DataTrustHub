namespace DataTrustHub.API.Organization.DTOs;

public record OrganizationResponseDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
