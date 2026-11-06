using DataTrustHub.Domain.Organizations;
using Xunit;

namespace DataTrustHub.Features.Tests.Domain;

// The Organization aggregate (#23), without HTTP or a database
public class OrganizationTests
{
    private const string OldName = "Acme";
    private const string NewName = "Acme Renamed";

    [Fact]
    public void Rename_NewName_ChangesTheName()
    {
        var organization = Organization.Create(OldName);

        organization.Rename(NewName);

        Assert.Equal(NewName, organization.Name);
    }

    // AC9: the organization stays the same one
    [Fact]
    public void Rename_NewName_KeepsTheId()
    {
        var organization = Organization.Create(OldName);
        var id = organization.Id;

        organization.Rename(NewName);

        Assert.Equal(id, organization.Id);
    }
}
