using DataTrustHub.Domain.Clearances;
using Xunit;

namespace DataTrustHub.Features.Tests.Domain;

// The Clearance aggregate (#23, UpdateClearance), without HTTP or a database: changing a clearance gives it a new
// name and classification level; its id, user and policy stay, and the new level is what Allows checks (C1)
public class ClearanceTests
{
    private const string OldName = "Secret clearance";
    private const string NewName = "Top Secret clearance";
    private const int LowPriority = 1;
    private const int HighPriority = 4;

    private static readonly ClassificationLevel LowLevel = new("Internal", LowPriority);
    private static readonly ClassificationLevel HighLevel = new("Top Secret", HighPriority);
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid PolicyId = Guid.NewGuid();

    [Fact]
    public void Change_NewNameAndLevel_ChangesTheNameAndTheLevel()
    {
        var clearance = Clearance.Grant(OldName, UserId, PolicyId, LowLevel);

        clearance.Change(NewName, HighLevel);

        Assert.Equal((NewName, HighLevel), (clearance.Name, clearance.Level));
    }

    [Fact]
    public void Change_NewNameAndLevel_KeepsTheIdTheUserAndThePolicy()
    {
        var clearance = Clearance.Grant(OldName, UserId, PolicyId, LowLevel);
        var id = clearance.Id;

        clearance.Change(NewName, HighLevel);

        Assert.Equal((id, UserId, PolicyId), (clearance.Id, clearance.UserId, clearance.PolicyId));
    }

    // C1 after raising the level
    [Fact]
    public void Change_RaisedLevel_AllowsTheNewPriority()
    {
        var clearance = Clearance.Grant(OldName, UserId, PolicyId, LowLevel);

        clearance.Change(OldName, HighLevel);

        Assert.True(clearance.Allows(PolicyId, HighPriority));
    }

    // C1 after lowering the level
    [Fact]
    public void Change_LoweredLevel_NoLongerAllowsAPriorityAboveIt()
    {
        var clearance = Clearance.Grant(OldName, UserId, PolicyId, HighLevel);

        clearance.Change(OldName, LowLevel);

        Assert.Equal((false, true), (clearance.Allows(PolicyId, LowPriority + 1), clearance.Allows(PolicyId, LowPriority)));
    }

    // C1: the policy stays, so a higher level still covers only its own policy
    [Fact]
    public void Change_RaisedLevel_StillAllowsNothingOnAnotherPolicy()
    {
        var clearance = Clearance.Grant(OldName, UserId, PolicyId, LowLevel);

        clearance.Change(OldName, HighLevel);

        Assert.False(clearance.Allows(Guid.NewGuid(), LowPriority));
    }
}
