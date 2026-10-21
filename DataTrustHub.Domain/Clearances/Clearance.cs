namespace DataTrustHub.Domain.Clearances;

// Grants a user a classification level on one policy
public sealed class Clearance
{
    private Clearance() { }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public Guid UserId { get; private set; }

    public Guid PolicyId { get; private set; }

    public ClassificationLevel Level { get; private set; } = null!;

    public static Clearance Grant(string name, Guid userId, Guid policyId, ClassificationLevel level) =>
        new() { Id = Guid.NewGuid(), Name = name, UserId = userId, PolicyId = policyId, Level = level };

    // True when this clearance lets its user read data classified at priority on policyId
    public bool Allows(Guid policyId, int priority) => PolicyId == policyId && Level.Priority >= priority;
}
