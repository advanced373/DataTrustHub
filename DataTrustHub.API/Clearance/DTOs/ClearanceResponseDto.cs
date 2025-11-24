using DataTrustHub.API.Shared.DTOs;

namespace DataTrustHub.API.Clearance.DTOs;

public record ClearanceResponseDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public Guid PolicyId { get; set; }
    public ClassificationLevelDto ClassificationLevel { get; set; } = new();
}
