using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.Policy
{
    public sealed class PolicyCreatedDomainEvent : IDomainEvent
    {
        public PolicyCreatedDomainEvent(Guid policyId)
        {
            PolicyId = policyId;
        }
        public Guid PolicyId { get; }
    }
}
