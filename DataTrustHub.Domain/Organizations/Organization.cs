namespace DataTrustHub.Domain.Organizations;

// An organization ("group" in the API and UI) that owns policies
public sealed class Organization
{
    // Rebuilds a stored entity; used by the persistence mappers only (internal, visible to Infrastructure)
    internal Organization(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public static Organization Create(string name) => new(Guid.NewGuid(), name);
}
