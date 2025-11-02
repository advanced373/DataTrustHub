using DataTrustHub.SharedKernel;
using MediatR;

namespace DataTrustHub.Application.Policy.Create
{
    public class CreatePolicyCommandHandler : IRequestHandler<CreatePolicyCommand, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(CreatePolicyCommand request, CancellationToken cancellationToken)
        {
            var policyId = Guid.NewGuid();
            // TODO: Persiste policy
            return await Task.FromResult(Result.Success(policyId));
        }
    }
}
