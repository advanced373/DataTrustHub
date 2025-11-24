using DataTrustHub.Application.Abstractions.Messaging;
using DataTrustHub.Domain.Policy;
using DataTrustHub.SharedKernel;
using MediatR;

namespace DataTrustHub.Application.Policy.Delete
{
    public class DeletePolicyCommandHandler : IRequestHandler<DeletePolicyCommand, Result>
    {
        private readonly IPolicyRepository _policyRepository;

        public DeletePolicyCommandHandler(IPolicyRepository policyRepository)
        {
            _policyRepository = policyRepository;
        }

        public async Task<Result> Handle(DeletePolicyCommand request, CancellationToken cancellationToken)
        {
            var policy = await _policyRepository.GetByIdAsync(request.PolicyId);

            if (policy is null)
            {
                return Result.Failure(PolicyErrors.NotFound(request.PolicyId));
            }

            await _policyRepository.DeleteAsync(request.PolicyId);
            return Result.Success();
        }
    }
}

