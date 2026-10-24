using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.DataItems;
using Xunit;

namespace DataTrustHub.Features.Tests.Domain;

// The DataItem rules on the entity, without HTTP or a database: upload within the owner's clearance,
// and the read rule D3' (ADR-0014): classified items are read only through a clearance, owner included
public class DataItemTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Reader = Guid.NewGuid();
    private static readonly Guid PolicyId = Guid.NewGuid();
    private static readonly ClassificationLevel Internal = ClassificationLevel.FindStandard(1)!;
    private static readonly ClassificationLevel Confidential = ClassificationLevel.FindStandard(2)!;
    private static readonly ClassificationLevel Secret = ClassificationLevel.FindStandard(3)!;
    private static readonly ClassificationLevel TopSecret = ClassificationLevel.FindStandard(4)!;

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
    public void IsReadableBy_ReaderAtOrAboveLevel_ReturnsTrue()
    {
        var item = Classified(Confidential);

        Assert.True(item.IsReadableBy(Reader, [Grant(Reader, Secret)]));
        Assert.True(item.IsReadableBy(Reader, [Grant(Reader, Confidential)]));
    }

    [Fact]
    public void IsReadableBy_ReaderBelowLevelOnAnotherPolicyOrWithOthersClearance_ReturnsFalse()
    {
        var item = Classified(Confidential);

        Assert.False(item.IsReadableBy(Reader, [Grant(Reader, Internal)]));
        Assert.False(item.IsReadableBy(Reader, [Clearance.Grant("other", Reader, Guid.NewGuid(), Secret)]));
        Assert.False(item.IsReadableBy(Reader, [Grant(Owner, Secret)]));
    }

    // D3': the owner of a classified item has no bypass, they read it only through a clearance
    [Fact]
    public void IsReadableBy_OwnerOfClassifiedWithoutClearance_ReturnsFalse()
    {
        var item = Classified(Confidential);

        Assert.False(item.IsReadableBy(Owner, []));
        Assert.False(item.IsReadableBy(Owner, [Grant(Reader, Secret)]));
    }

    [Fact]
    public void IsReadableBy_OwnerOfClassifiedWithClearanceBelowLevel_ReturnsFalse()
    {
        var item = Classified(Confidential);

        Assert.False(item.IsReadableBy(Owner, [Grant(Owner, Internal)]));
    }

    [Fact]
    public void IsReadableBy_OwnerOfClassifiedWithClearanceOnAnotherPolicy_ReturnsFalse()
    {
        var item = Classified(Confidential);

        Assert.False(item.IsReadableBy(Owner, [Clearance.Grant("other", Owner, Guid.NewGuid(), Secret)]));
    }

    [Fact]
    public void IsReadableBy_OwnerOfClassifiedWithClearanceAtOrAboveLevel_ReturnsTrue()
    {
        var item = Classified(Confidential);

        Assert.True(item.IsReadableBy(Owner, [Grant(Owner, Confidential)]));
        Assert.True(item.IsReadableBy(Owner, [Grant(Owner, Secret)]));
    }

    [Fact]
    public void IsReadableBy_PersonalItemOwnerWithoutClearance_ReturnsTrue()
    {
        var item = DataItem.CreatePersonal("p.txt", 1, "eA==", Owner);

        Assert.True(item.IsReadableBy(Owner, []));
    }

    [Fact]
    public void IsReadableBy_PersonalItemNonOwnerWithClearances_ReturnsFalse()
    {
        var item = DataItem.CreatePersonal("p.txt", 1, "eA==", Owner);

        Assert.False(item.IsReadableBy(Reader, [Grant(Reader, Secret)]));
        Assert.False(item.IsReadableBy(Reader, [Grant(Reader, TopSecret), Grant(Owner, TopSecret)]));
    }

    [Fact]
    public void IsReadableBy_DeletedClassifiedItem_ReturnsFalseForEveryoneIncludingClearedOwner()
    {
        var item = Classified(Confidential);
        item.MarkDeleted(Owner);

        Assert.False(item.IsReadableBy(Owner, [Grant(Owner, TopSecret)]));
        Assert.False(item.IsReadableBy(Reader, [Grant(Reader, TopSecret)]));
    }

    [Fact]
    public void IsReadableBy_DeletedPersonalItem_ReturnsFalseForOwner()
    {
        var item = DataItem.CreatePersonal("p.txt", 1, "eA==", Owner);
        item.MarkDeleted(Owner);

        Assert.False(item.IsReadableBy(Owner, []));
    }

    [Fact]
    public void MarkDeleted_OnlyOwnerAndOnlyOnce_ThenUnreadable()
    {
        var item = Classified(Confidential);

        Assert.Equal(DataItemErrors.NotFound, item.MarkDeleted(Reader).Error);
        Assert.True(item.MarkDeleted(Owner).IsSuccess);
        Assert.Equal(DataItemErrors.NotFound, item.MarkDeleted(Owner).Error);
        Assert.False(item.IsReadableBy(Owner, [Grant(Owner, Secret)]));
    }

    private static DataItem Classified(ClassificationLevel level) =>
        DataItem.CreateClassified("r.txt", 1, "eA==", Owner, PolicyId, level, [Grant(Owner, Secret)]).Value;

    private static Clearance Grant(Guid userId, ClassificationLevel level) =>
        Clearance.Grant("test", userId, PolicyId, level);
}
