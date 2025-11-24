namespace DataTrustHub.API.Shared.DTOs;

public record ClassificationLevelDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Priority { get; set; }
}
