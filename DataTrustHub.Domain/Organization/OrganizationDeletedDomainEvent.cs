using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.Organization
{
    public sealed record OrganizationDeletedDomainEvent(Guid OrganizationId) : IDomainEvent;
}
