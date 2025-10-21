using MediatR;

namespace DataTrustHub.Application.Policy.Create
{
    public class CreatePolicyCommandHandler : IRequestHandler<CreatePolicyCommand, Guid>
    {
        public async Task<Guid> Handle(CreatePolicyCommand request, CancellationToken cancellationToken)
        {
            var policyId = Guid.NewGuid();
            // TODO: Persiste policy
            return await Task.FromResult(policyId);
        }
    }
}
