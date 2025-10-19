using DataTrustHub.SharedKernel;

namespace DataTrustHub.Domain.User
{
    public sealed record UserRegisteredDomainEvent(Guid UserId): IDomainEvent;
}
