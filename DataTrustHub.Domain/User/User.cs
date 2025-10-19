using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.User
{
    public sealed class User: Entity
    {
        public required Guid Id { get; set; }
        public required string Email { get; set; }
        public required string FirstName { get; set; }
        public required string LastName { get; set; }
        public required string PasswordHash { get; set; }
        public Guid OrganizationId { get; set; }
    }
}
