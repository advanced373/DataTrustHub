using DataTrustHub.Application.Abstractions.Messaging;
using DataTrustHub.Domain.Policy;
using DataTrustHub.SharedKernel;
using PolicyValue = DataTrustHub.Domain.Policy.Policy;

namespace DataTrustHub.Application.Policy.Get;

public sealed class GetPolicyByIdQueryHandler : IQueryHandler<GetPolicyByIdQuery, PolicyValue>
{
    private readonly IPolicyRepository _policyRepository;

    public GetPolicyByIdQueryHandler(IPolicyRepository policyRepository)
    {
        _policyRepository = policyRepository;
    }

    public async Task<Result<PolicyValue>> Handle(GetPolicyByIdQuery request, CancellationToken cancellationToken)
    {
        var policy = await _policyRepository.GetByIdAsync(request.PolicyId);

        return policy is null
            ? Result.Failure<PolicyValue>(Error.NotFound("Policy.NotFound", $"Policy with ID {request.PolicyId} was not found."))
            : Result.Success(policy);
    }
}
