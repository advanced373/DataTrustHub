using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.Policy
{
    public sealed class Policy : Entity, IAggregateRoot
    {
        public required Guid Id { get; set; }
        public required string Name { get; set; }
        public required Guid OrganizationId { get; set; }
    }
}
