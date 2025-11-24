using DataTrustHub.API.Shared.DTOs;

namespace DataTrustHub.API.Policy.DTOs;

public record PolicyResponseDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid OrganizationId { get; set; }
    public List<ClassificationLevelDto> ClassificationLevels { get; set; } = [];
}
