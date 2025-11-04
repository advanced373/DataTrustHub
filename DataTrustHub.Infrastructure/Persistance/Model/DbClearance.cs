namespace DataTrustHub.Infrastructure.Persistance.Model
{
    public class DbClearance: DbEntity
    {
        public required string Name { get; set; }
        public required Guid PolicyId { get; set; }
        public required Guid UserId { get; set; }
    }
}
