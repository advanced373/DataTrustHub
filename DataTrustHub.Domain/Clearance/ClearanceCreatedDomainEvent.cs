using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.Clearance
{
    public sealed class ClearanceCreatedDomainEvent : IDomainEvent
    {
        public ClearanceCreatedDomainEvent(Guid clearanceId)
        {
            ClearanceId = clearanceId;
        }
        public Guid ClearanceId { get; }
    }
}
