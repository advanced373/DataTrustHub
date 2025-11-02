namespace DataTrustHub.Infrastructure.Persistance.Model.User
{
    public class DbUser
    {
        public required Guid Id { get; set; }
        public required string Email { get; set; }
        public required string HashedPassword { get; set; }
    }
}
