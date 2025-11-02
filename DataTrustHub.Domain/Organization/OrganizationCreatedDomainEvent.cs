using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.Organization
{
    public sealed record OrganizationCreatedDomainEvent(Guid OrganizationId): IDomainEvent;
}
