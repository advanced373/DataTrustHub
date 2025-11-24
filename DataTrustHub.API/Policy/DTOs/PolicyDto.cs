namespace DataTrustHub.API.Policy.DTOs
{
    public record PolicyDto
    {
        public string Name { get; set; } = string.Empty;
        public Guid OrganizationId { get; set; }
    }
}

