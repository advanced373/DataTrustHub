using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.DataItems;
using Xunit;

namespace DataTrustHub.Features.Tests.Domain;

// The ADR-0011 rules on the entity, without HTTP or a database
public class DataItemTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Reader = Guid.NewGuid();
    private static readonly Guid PolicyId = Guid.NewGuid();
    private static readonly ClassificationLevel Confidential = ClassificationLevel.FindStandard(2)!;
    private static readonly ClassificationLevel Secret = ClassificationLevel.FindStandard(3)!;

    [Fact]
    public void CreateClassified_WithinOwnersClearance_SetsPolicyLevelAndMarking()
    {
        var result = DataItem.CreateClassified("r.txt", 1, "eA==", Owner, PolicyId, Confidential, [Grant(Owner, Secret)]);

        Assert.True(result.IsSuccess);
        Assert.Equal((PolicyId, 2, "Confidential"), (result.Value.PolicyId, result.Value.ClassificationPriority, result.Value.SecurityMarking));
    }

    [Fact]
    public void CreateClassified_AboveOwnersClearanceOrOnAnotherPolicy_Fails()
    {
        var above = DataItem.CreateClassified("r.txt", 1, "eA==", Owner, PolicyId, Secret, [Grant(Owner, Confidential)]);
        var otherPolicy = DataItem.CreateClassified("r.txt", 1, "eA==", Owner, Guid.NewGuid(), Confidential, [Grant(Owner, Secret)]);

        Assert.Equal(DataItemErrors.NotCleared, above.Error);
        Assert.Equal(DataItemErrors.NotCleared, otherPolicy.Error);
    }

    [Fact]
    public void IsReadableBy_FollowsOwnerAndClearanceRule()
    {
        var item = Classified(Confidential);

        Assert.True(item.IsReadableBy(Owner, []));
        Assert.True(item.IsReadableBy(Reader, [Grant(Reader, Secret)]));
        Assert.True(item.IsReadableBy(Reader, [Grant(Reader, Confidential)]));
        Assert.False(item.IsReadableBy(Reader, [Grant(Reader, ClassificationLevel.FindStandard(1)!)]));
        Assert.False(item.IsReadableBy(Reader, [Clearance.Grant("other", Reader, Guid.NewGuid(), Secret)]));
        Assert.False(item.IsReadableBy(Reader, [Grant(Owner, Secret)]));
    }

    [Fact]
    public void PersonalItem_IsReadableOnlyByOwner()
    {
        var item = DataItem.CreatePersonal("p.txt", 1, "eA==", Owner);

        Assert.True(item.IsReadableBy(Owner, []));
        Assert.False(item.IsReadableBy(Reader, [Grant(Reader, Secret)]));
    }

    [Fact]
    public void MarkDeleted_OnlyOwnerAndOnlyOnce_ThenUnreadable()
    {
        var item = Classified(Confidential);

        Assert.Equal(DataItemErrors.NotFound, item.MarkDeleted(Reader).Error);
        Assert.True(item.MarkDeleted(Owner).IsSuccess);
        Assert.Equal(DataItemErrors.NotFound, item.MarkDeleted(Owner).Error);
        Assert.False(item.IsReadableBy(Owner, []));
    }

    private static DataItem Classified(ClassificationLevel level) =>
        DataItem.CreateClassified("r.txt", 1, "eA==", Owner, PolicyId, level, [Grant(Owner, Secret)]).Value;

    private static Clearance Grant(Guid userId, ClassificationLevel level) =>
        Clearance.Grant("test", userId, PolicyId, level);
}
