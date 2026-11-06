using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.ReadModels.ClassificationOptions;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.DataManagement.ListClassificationOptions;

public record ListClassificationOptionsQuery(Guid UserId);

public class ListClassificationOptionsHandler(IClassificationOptionQueries classificationOptionQueries)
    : IQueryHandler<ListClassificationOptionsQuery, Result<ClassificationOptionsResponse>>
{
    private readonly IClassificationOptionQueries _classificationOptionQueries = classificationOptionQueries;

    // One entry per policy the user holds a clearance on, with their highest priority there
    public async Task<Result<ClassificationOptionsResponse>> Handle(
        ListClassificationOptionsQuery query,
        CancellationToken cancellationToken)
    {
        var clearedPolicies = await _classificationOptionQueries.ListClearedPoliciesAsync(query.UserId, cancellationToken);
        var policies = clearedPolicies.Select(ToOption).ToList();
        return Result.Success(new ClassificationOptionsResponse(StandardLevels(), policies));
    }

    private static ClassificationPolicyOption ToOption(ClearedPolicy policy) =>
        new(policy.PolicyId, policy.PolicyName, policy.OrganizationName, policy.MaxPriority);

    private static IReadOnlyList<ClassificationLevelOption> StandardLevels() =>
        ClassificationLevel.Standard.Select(level => new ClassificationLevelOption(level.Name, level.Priority)).ToList();
}
