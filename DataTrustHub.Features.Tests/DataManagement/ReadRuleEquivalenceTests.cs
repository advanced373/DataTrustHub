using DataTrustHub.Features.DataManagement;
using DataTrustHub.Features.Tests.DataManagement.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DataTrustHub.Features.Tests.DataManagement.Helpers.ReadRuleScenario;

namespace DataTrustHub.Features.Tests.DataManagement;

// The read rule (ADR-0014, invariant D3') has an entity form, DataItem.IsReadableBy, and a SQL form,
// DataAccess.VisibleTo. These tests keep the two forms in line on one shared scenario (#99).
public class ReadRuleEquivalenceTests(ReadRuleScenario scenario) : IClassFixture<ReadRuleScenario>
{
    private const string PersonalOwnerBranch = "personal, owner";
    private const string ClassifiedOwnerBranch = "classified, owner reads only through a clearance (no bypass)";
    private const string DeletedBranch = "deleted";
    private const string PersonalNonOwnerBranch = "personal, non-owner";
    private const string OtherPolicyBranch = "clearance on another policy";
    private const string BelowLevelBranch = "clearance below level";
    private const string EqualLevelBranch = "clearance at equal level";
    private const string AboveLevelBranch = "clearance above level";
    private const string NeedToKnowBranch = "admin without clearance (need-to-know)";
    private const string OtherUsersClearanceBranch = "clearance held by another user";

    private static readonly string[] Branches =
    [
        PersonalOwnerBranch, ClassifiedOwnerBranch, DeletedBranch, PersonalNonOwnerBranch, OtherPolicyBranch,
        BelowLevelBranch, EqualLevelBranch, AboveLevelBranch, NeedToKnowBranch, OtherUsersClearanceBranch
    ];

    // Each branch of the rule, with at least one true and one false outcome
    public static TheoryData<string, string, string, bool> BranchCases => new()
    {
        { PersonalOwnerBranch, Owner, OwnerPersonal, true },
        { PersonalOwnerBranch, RevokedOwner, RevokedOwnerPersonal, true },
        { PersonalOwnerBranch, Owner, OwnerPersonalDeleted, false },
        // D3': owning a classified item gives nothing; the owner's own clearance decides
        { ClassifiedOwnerBranch, RevokedOwner, RevokedOwnerApolloConfidential, false },
        { ClassifiedOwnerBranch, DemotedOwner, DemotedOwnerApolloConfidential, false },
        { ClassifiedOwnerBranch, OtherPolicyOwner, OtherPolicyOwnerApolloConfidential, false },
        { ClassifiedOwnerBranch, Owner, ApolloTopSecret, true },
        { ClassifiedOwnerBranch, Owner, ApolloConfidential, true },
        { ClassifiedOwnerBranch, ApolloSecretReader, ReaderApolloConfidential, true },
        { DeletedBranch, ApolloSecretReader, ApolloSecret, true },
        { DeletedBranch, ApolloSecretReader, ApolloSecretDeleted, false },
        { DeletedBranch, Owner, OwnerPersonalDeleted, false },
        { DeletedBranch, HrTopSecretReader, HrSecretDeleted, false },
        { PersonalNonOwnerBranch, ApolloSecretReader, ReaderPersonal, true },
        { PersonalNonOwnerBranch, ApolloSecretReader, OwnerPersonal, false },
        { PersonalNonOwnerBranch, Owner, ReaderPersonal, false },
        { PersonalNonOwnerBranch, HrTopSecretReader, RevokedOwnerPersonal, false },
        { OtherPolicyBranch, HrTopSecretReader, HrInternal, true },
        { OtherPolicyBranch, HrTopSecretReader, ApolloPublic, false },
        { OtherPolicyBranch, ApolloSecretReader, HrInternal, false },
        { BelowLevelBranch, InternalReader, ApolloPublic, true },
        { BelowLevelBranch, InternalReader, ApolloConfidential, false },
        { BelowLevelBranch, ApolloSecretReader, ApolloTopSecret, false },
        { BelowLevelBranch, InternalReader, RevokedOwnerApolloConfidential, false },
        { EqualLevelBranch, ApolloSecretReader, ApolloSecret, true },
        { EqualLevelBranch, InternalReader, HrInternal, true },
        // An equal or higher level never denies by itself: these false rows hold that level on another policy
        { EqualLevelBranch, HrTopSecretReader, ApolloTopSecret, false },
        { AboveLevelBranch, ApolloSecretReader, ApolloConfidential, true },
        { AboveLevelBranch, Owner, ReaderApolloConfidential, true },
        { AboveLevelBranch, HrTopSecretReader, ApolloConfidential, false },
        // The owner losing their clearance does not hide the item from others who hold one
        { AboveLevelBranch, ApolloSecretReader, RevokedOwnerApolloConfidential, true },
        { AboveLevelBranch, Owner, DemotedOwnerApolloConfidential, true },
        // The entity form gets every user's clearances; only the user's own may count
        { OtherUsersClearanceBranch, InternalReader, HrInternal, true },
        { OtherUsersClearanceBranch, UnclearedUser, HrInternal, false },
        { OtherUsersClearanceBranch, UnclearedUser, ApolloPublic, false },
        { OtherUsersClearanceBranch, Admin, ApolloTopSecret, false },
        { NeedToKnowBranch, Admin, AdminPersonal, true },
        { NeedToKnowBranch, Admin, ApolloPublic, false },
        { NeedToKnowBranch, Admin, OwnerPersonal, false }
    };

    [Fact]
    public void Scenario_SqlForm_RunsOnARelationalProvider()
    {
        using var db = scenario.CreateContext();

        Assert.True(db.Database.IsRelational());
    }

    [Fact]
    public async Task ReadRule_EveryUserAndDataItemPair_EntityFormEqualsSqlForm()
    {
        var mismatches = new List<string>();

        foreach (var userKey in scenario.Users.Keys)
        {
            var visibleIds = await VisibleIdsAsync(userKey);
            mismatches.AddRange(scenario.DataItems
                .Select(entry => (
                    ItemKey: entry.Key,
                    Entity: IsReadable(userKey, entry.Key),
                    Sql: visibleIds.Contains(entry.Value.Id)))
                .Where(pair => pair.Entity != pair.Sql)
                .Select(pair => $"user '{userKey}', data item '{pair.ItemKey}': "
                    + $"IsReadableBy = {pair.Entity}, in VisibleTo = {pair.Sql}"));
        }

        Assert.True(mismatches.Count == 0,
            "The entity form and the SQL form of the read rule disagree:" + Environment.NewLine
            + string.Join(Environment.NewLine, mismatches));
    }

    [Theory]
    [MemberData(nameof(BranchCases))]
    public async Task ReadRule_BranchCase_BothFormsGiveExpectedOutcome(
        string branch, string userKey, string itemKey, bool expected)
    {
        var entityForm = IsReadable(userKey, itemKey);
        var sqlForm = (await VisibleIdsAsync(userKey)).Contains(scenario.DataItems[itemKey].Id);

        Assert.True(entityForm == expected && sqlForm == expected,
            $"[{branch}] user '{userKey}', data item '{itemKey}': expected {expected}, "
            + $"IsReadableBy = {entityForm}, in VisibleTo = {sqlForm}");
    }

    [Fact]
    public void BranchCases_EveryBranch_HasATrueAndAFalseOutcome()
    {
        var outcomesByBranch = BranchCases
            .GroupBy(row => (string)row[0], row => (bool)row[3])
            .ToDictionary(group => group.Key, group => group.Distinct().Count());

        Assert.All(Branches, branch => Assert.Equal(2, outcomesByBranch.GetValueOrDefault(branch)));
    }

    // Passes every user's clearances, so the entity form must filter by user just as the SQL form does
    private bool IsReadable(string userKey, string itemKey) =>
        scenario.DataItems[itemKey].IsReadableBy(scenario.Users[userKey].Id, scenario.AllClearances);

    private async Task<HashSet<Guid>> VisibleIdsAsync(string userKey)
    {
        await using var db = scenario.CreateContext();
        var userId = scenario.Users[userKey].Id;

        var visibleIds = await db.DataItems.AsNoTracking()
            .VisibleTo(db, userId)
            .Select(item => item.Id)
            .ToListAsync();

        return visibleIds.ToHashSet();
    }
}
