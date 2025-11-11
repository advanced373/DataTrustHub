using DataTrustHub.Domain.Policy;
using PolicyValue = DataTrustHub.Domain.Policy.Policy;
using DataTrustHub.SharedKernel;
using MediatR;

namespace DataTrustHub.Application.Policy.Create
{
    public class CreatePolicyCommandHandler : IRequestHandler<CreatePolicyCommand, Result<Guid>>
    {
        private readonly IPolicyRepository _policyRepository;

        public CreatePolicyCommandHandler(IPolicyRepository policyRepository)
        {
            _policyRepository = policyRepository;
        }

        public async Task<Result<Guid>> Handle(CreatePolicyCommand request, CancellationToken cancellationToken)
        {
            var policyId = Guid.NewGuid();
            var policy = new PolicyValue
            {
                Id = policyId,
                Name = request.Name,
                OrganizationId = request.OrganizationId,
                ClassificationLevels = []
            };
            
            await _policyRepository.AddAsync(policy);
            return Result.Success(policyId);
        }
    }
}
