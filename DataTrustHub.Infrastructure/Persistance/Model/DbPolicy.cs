namespace DataTrustHub.Infrastructure.Persistance.Model
{
    public class DbPolicy: DbEntity
    {
        public required string Name { get; set; }
        public required Guid OrganizationId { get; set; }
    }
}
