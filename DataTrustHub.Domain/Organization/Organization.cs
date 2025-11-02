using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.Organization
{
    public sealed class Organization : Entity, IAggregateRoot
    {
        public required Guid Id { get; set; }
        public required string Name { get; set; }
    }
}
