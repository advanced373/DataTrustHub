using DataTrustHub.Application.Abstractions.Messaging;
using DataTrustHub.Domain.Policy;
using DataTrustHub.SharedKernel;
using PolicyValue = DataTrustHub.Domain.Policy.Policy;

namespace DataTrustHub.Application.Policy.Get;

public sealed class GetPoliciesQueryHandler : IQueryHandler<GetPoliciesQuery, List<PolicyValue>>
{
    private readonly IPolicyRepository _policyRepository;

    public GetPoliciesQueryHandler(IPolicyRepository policyRepository)
    {
        _policyRepository = policyRepository;
    }

    public async Task<Result<List<PolicyValue>>> Handle(GetPoliciesQuery request, CancellationToken cancellationToken)
    {
        var policies = await _policyRepository.GetAllAsync();
        return Result.Success(policies);
    }
}
