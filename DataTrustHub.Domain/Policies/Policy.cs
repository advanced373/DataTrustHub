namespace DataTrustHub.Domain.Policies;

// A data access policy of an organization; clearances grant users a level on it
public sealed class Policy
{
    private Policy() { }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public Guid OrganizationId { get; private set; }

    public static Policy Create(string name, Guid organizationId) =>
        new() { Id = Guid.NewGuid(), Name = name, OrganizationId = organizationId };
}
