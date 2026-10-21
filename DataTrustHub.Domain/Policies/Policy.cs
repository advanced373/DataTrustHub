namespace DataTrustHub.Domain.Policies;

// A data access policy of an organization; clearances grant users a level on it
public sealed class Policy
{
    // Rebuilds a stored entity; used by the persistence mappers only (internal, visible to Infrastructure)
    internal Policy(Guid id, string name, Guid organizationId)
    {
        Id = id;
        Name = name;
        OrganizationId = organizationId;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public Guid OrganizationId { get; private set; }

    public static Policy Create(string name, Guid organizationId) => new(Guid.NewGuid(), name, organizationId);
}
