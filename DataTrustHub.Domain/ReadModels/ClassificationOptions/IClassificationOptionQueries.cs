namespace DataTrustHub.Domain.ReadModels.ClassificationOptions;

// The read side of the classification options (ADR-0012 step 4): what a user may classify an upload under
public interface IClassificationOptionQueries
{
    // One entry per policy the user holds a clearance on, with their highest priority there, ordered by
    // organization name, then policy name
    Task<IReadOnlyList<ClearedPolicy>> ListClearedPoliciesAsync(Guid userId, CancellationToken cancellationToken);
}
