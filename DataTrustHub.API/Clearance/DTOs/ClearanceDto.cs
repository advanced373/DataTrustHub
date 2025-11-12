namespace DataTrustHub.API.Clearance.DTOs
{
    public record ClearanceDto
    {
        public Guid UserId { get; set; }
        public Guid PolicyId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ClassificationLevelName { get; set; } = string.Empty;
        public int ClassificationLevelPriority { get; set; }
    }
}

