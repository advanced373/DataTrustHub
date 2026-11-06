using DataTrustHub.Domain.Policies;
using Xunit;

namespace DataTrustHub.Features.Tests.Domain;

// The Policy aggregate (#23, RenamePolicy), without HTTP or a database
public class PolicyTests
{
    private const string OldName = "Project Apollo";
    private const string NewName = "Project Artemis";

    private static readonly Guid OrganizationId = Guid.NewGuid();

    [Fact]
    public void Rename_NewName_ChangesTheName()
    {
        var policy = Policy.Create(OldName, OrganizationId);

        policy.Rename(NewName);

        Assert.Equal(NewName, policy.Name);
    }

    // AC10: the policy stays the same one, in the same organization
    [Fact]
    public void Rename_NewName_KeepsTheIdAndTheOrganization()
    {
        var policy = Policy.Create(OldName, OrganizationId);
        var id = policy.Id;

        policy.Rename(NewName);

        Assert.Equal((id, OrganizationId), (policy.Id, policy.OrganizationId));
    }
}
