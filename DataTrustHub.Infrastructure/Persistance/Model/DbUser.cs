using DataTrustHub.SharedKernel;

namespace DataTrustHub.Infrastructure.Persistance.Model
{
    public class DbUser: DbEntity
    {
        public required string Email { get; set; }
        public required string HashedPassword { get; set; }
        public string Role { get; set; } = UserRoles.User;
    }
}
