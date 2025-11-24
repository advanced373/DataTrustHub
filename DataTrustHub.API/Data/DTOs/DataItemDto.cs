namespace DataTrustHub.API.Data.DTOs
{
    public record DataItemDto
    {
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; }
        public string? Content { get; set; }
        public Guid OwnerUserId { get; set; }
        public string SecurityMarking { get; set; } = string.Empty;
    }
}

