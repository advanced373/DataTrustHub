using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.User
{
    public sealed class User : Entity, IAggregateRoot
    {
        public required Guid Id { get; set; }
        public required string Email { get; set; }
        public required string HashedPassword { get; set; }
        public List<Guid> ClearanceIds { get; set; } = new();
    }
}
