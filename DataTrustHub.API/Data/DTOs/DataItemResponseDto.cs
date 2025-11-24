namespace DataTrustHub.API.Data.DTOs;

public record DataItemResponseDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    public string? Content { get; set; }
    public Guid OwnerUserId { get; set; }
    public string SecurityMarking { get; set; } = string.Empty;
}
