using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.Data
{
    public sealed class DataCreatedDomainEvent : IDomainEvent
    {
        public DataCreatedDomainEvent(Guid dataId)
        {
            DataId = dataId;
        }
        public Guid DataId { get; }
    }
}
