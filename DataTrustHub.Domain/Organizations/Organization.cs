namespace DataTrustHub.Domain.Organizations;

// An organization ("group" in the API and UI) that owns policies
public sealed class Organization
{
    private Organization() { }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public static Organization Create(string name) => new() { Id = Guid.NewGuid(), Name = name };
}
