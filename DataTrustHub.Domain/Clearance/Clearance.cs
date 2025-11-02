using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.Clearance
{
    public sealed class Clearance : Entity
    {
        public required Guid Id { get; set; }
        public required string Name { get; set; }
        public required Guid PolicyId { get; set; }
        public required Guid UserId { get; set; }
    }
}
