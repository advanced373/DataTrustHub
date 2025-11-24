using DataTrustHub.Application.Abstractions.Messaging;

namespace DataTrustHub.Application.Policy.Delete
{
    public record DeletePolicyCommand(Guid PolicyId) : ICommand;
}

