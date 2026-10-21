namespace DataTrustHub.Domain.Clearances;

// Grants a user a classification level on one policy
public sealed class Clearance
{
    // Rebuilds a stored entity; used by the persistence mappers only (internal, visible to Infrastructure)
    internal Clearance(Guid id, string name, Guid userId, Guid policyId, ClassificationLevel level)
    {
        Id = id;
        Name = name;
        UserId = userId;
        PolicyId = policyId;
        Level = level;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public Guid UserId { get; private set; }

    public Guid PolicyId { get; private set; }

    public ClassificationLevel Level { get; private set; }

    public static Clearance Grant(string name, Guid userId, Guid policyId, ClassificationLevel level) =>
        new(Guid.NewGuid(), name, userId, policyId, level);

    // True when this clearance lets its user read data classified at priority on policyId
    public bool Allows(Guid policyId, int priority) => PolicyId == policyId && Level.Priority >= priority;
}
