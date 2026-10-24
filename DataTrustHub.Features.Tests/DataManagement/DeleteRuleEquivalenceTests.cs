using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.DataItems;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.Policies;
using DataTrustHub.Features.DataManagement;
using DataTrustHub.Features.DataManagement.DeleteDataItem;
using DataTrustHub.Features.Tests.DataManagement.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DataTrustHub.Features.Tests.DataManagement.Helpers.DeleteRuleScenario;

namespace DataTrustHub.Features.Tests.DataManagement;

// The delete rule (ADR-0014, invariant D5') has an entity form, DataItem.MarkDeleted with the rights the use case
// DeleteDataItem gathers, and a SQL form, DataAccess.DeletableBy, behind the "can delete" flag of the list. These
// tests keep the two forms in line on one shared scenario. The entity form runs the real handler on a copy of the
// item loaded through the repositories, with a unit of work that never saves, so the scenario stays unchanged.
public class DeleteRuleEquivalenceTests(DeleteRuleScenario scenario) : IClassFixture<DeleteRuleScenario>
{
    private const string PersonalOwnerBranch = "personal, owner";
    private const string PersonalNonOwnerBranch = "personal, non-owner (an organization admin included)";
    private const string DeletedBranch = "deleted";
    private const string OwnerMemberBranch = "classified, owner who is a member (any role)";
    private const string OwnerWhoLeftBranch = "classified, owner who left the organization";
    private const string OrgAdminBranch = "classified, organization admin of its organization";
    private const string MemberNonOwnerBranch = "classified, member who is not the owner";
    private const string OutsiderBranch = "outsider";
    private const string PolicyGoneBranch = "classified, policy gone: owner only";
    private const string TwoOrganizationsBranch = "rights in one organization only count there";

    private static readonly string[] Branches =
    [
        PersonalOwnerBranch, PersonalNonOwnerBranch, DeletedBranch, OwnerMemberBranch, OwnerWhoLeftBranch,
        OrgAdminBranch, MemberNonOwnerBranch, OutsiderBranch, PolicyGoneBranch, TwoOrganizationsBranch
    ];

    // Each branch of the rule, with at least one true and one false outcome
    public static TheoryData<string, string, string, bool> BranchCases => new()
    {
        { PersonalOwnerBranch, AlphaMember, MemberPersonal, true },
        { PersonalOwnerBranch, FormerMember, FormerMemberPersonal, true },
        { PersonalOwnerBranch, Outsider, OutsiderPersonal, true },
        { PersonalOwnerBranch, AlphaMember, MemberPersonalDeleted, false },
        { PersonalNonOwnerBranch, AlphaOrgAdmin, MemberPersonal, false },
        { PersonalNonOwnerBranch, AlphaMember, OrgAdminPersonal, false },
        { PersonalNonOwnerBranch, AlphaOrgAdmin, OrgAdminPersonal, true },
        { DeletedBranch, AlphaMember, MemberAlphaDeleted, false },
        { DeletedBranch, AlphaOrgAdmin, MemberAlphaDeleted, false },
        { DeletedBranch, AlphaMember, MemberPersonalDeleted, false },
        { DeletedBranch, AlphaMember, MemberAlpha, true },
        { OwnerMemberBranch, AlphaMember, MemberAlpha, true },
        { OwnerMemberBranch, AlphaGuest, GuestAlpha, true },
        { OwnerMemberBranch, AlphaOrgAdmin, OrgAdminAlpha, true },
        { OwnerMemberBranch, AlphaMemberBetaOrgAdmin, TwoOrgUserAlpha, true },
        { OwnerMemberBranch, AlphaMember, MemberAlphaDeleted, false },
        { OwnerWhoLeftBranch, FormerMember, FormerMemberAlpha, false },
        // The item stays with the organization: its organization admin may still delete it
        { OwnerWhoLeftBranch, AlphaOrgAdmin, FormerMemberAlpha, true },
        // No clearance is stored in this scenario: organization admins delete items they cannot read
        { OrgAdminBranch, AlphaOrgAdmin, MemberAlpha, true },
        { OrgAdminBranch, AlphaOrgAdmin, GuestAlpha, true },
        { OrgAdminBranch, BetaOrgAdmin, TwoOrgUserBeta, true },
        { OrgAdminBranch, AlphaMemberBetaOrgAdmin, BetaOrgAdminBeta, true },
        { OrgAdminBranch, BetaOrgAdmin, MemberAlpha, false },
        { OrgAdminBranch, AlphaOrgAdmin, BetaOrgAdminBeta, false },
        { MemberNonOwnerBranch, AlphaMember, GuestAlpha, false },
        { MemberNonOwnerBranch, AlphaGuest, MemberAlpha, false },
        { MemberNonOwnerBranch, AlphaMemberBetaOrgAdmin, MemberAlpha, false },
        { MemberNonOwnerBranch, AlphaMember, OrgAdminAlpha, false },
        { MemberNonOwnerBranch, AlphaGuest, GuestAlpha, true },
        { OutsiderBranch, Outsider, MemberAlpha, false },
        { OutsiderBranch, Outsider, BetaOrgAdminBeta, false },
        { OutsiderBranch, Outsider, OutsiderGonePolicy, true },
        { PolicyGoneBranch, AlphaMember, MemberGonePolicy, true },
        { PolicyGoneBranch, Outsider, OutsiderGonePolicy, true },
        { PolicyGoneBranch, AlphaOrgAdmin, MemberGonePolicy, false },
        { PolicyGoneBranch, AlphaMember, OutsiderGonePolicy, false },
        { TwoOrganizationsBranch, AlphaMemberBetaOrgAdmin, TwoOrgUserBeta, true },
        { TwoOrganizationsBranch, BetaOrgAdmin, TwoOrgUserAlpha, false },
        { TwoOrganizationsBranch, AlphaOrgAdmin, TwoOrgUserBeta, false },
        { TwoOrganizationsBranch, FormerMember, MemberAlpha, false }
    };

    [Fact]
    public async Task Scenario_SqlForm_RunsOnARelationalProvider()
    {
        using var scope = scenario.CreateScope();

        Assert.True(scope.ServiceProvider.GetRequiredService<DContext>().Database.IsRelational());
        Assert.NotEmpty(await DeletableIdsAsync(AlphaOrgAdmin));
    }

    [Fact]
    public async Task DeleteRule_EveryUserAndDataItemPair_EntityFormEqualsSqlForm()
    {
        var mismatches = new List<string>();

        foreach (var userKey in scenario.Users.Keys)
        {
            var deletableIds = await DeletableIdsAsync(userKey);
            foreach (var (itemKey, item) in scenario.DataItems)
            {
                var entityForm = await EntityFormAcceptsAsync(userKey, itemKey);
                var sqlForm = deletableIds.Contains(item.Id);
                if (entityForm != sqlForm)
                    mismatches.Add($"user '{userKey}', data item '{itemKey}': "
                        + $"MarkDeleted accepts = {entityForm}, in DeletableBy = {sqlForm}");
            }
        }

        Assert.True(mismatches.Count == 0,
            "The entity form and the SQL form of the delete rule disagree:" + Environment.NewLine
            + string.Join(Environment.NewLine, mismatches));
    }

    [Theory]
    [MemberData(nameof(BranchCases))]
    public async Task DeleteRule_BranchCase_BothFormsGiveExpectedOutcome(
        string branch, string userKey, string itemKey, bool expected)
    {
        var entityForm = await EntityFormAcceptsAsync(userKey, itemKey);
        var sqlForm = (await DeletableIdsAsync(userKey)).Contains(scenario.DataItems[itemKey].Id);

        Assert.True(entityForm == expected && sqlForm == expected,
            $"[{branch}] user '{userKey}', data item '{itemKey}': expected {expected}, "
            + $"MarkDeleted accepts = {entityForm}, in DeletableBy = {sqlForm}");
    }

    [Fact]
    public void BranchCases_EveryBranch_HasATrueAndAFalseOutcome()
    {
        var outcomesByBranch = BranchCases
            .GroupBy(row => (string)row[0], row => (bool)row[3])
            .ToDictionary(group => group.Key, group => group.Distinct().Count());

        Assert.All(Branches, branch => Assert.Equal(2, outcomesByBranch.GetValueOrDefault(branch)));
    }

    [Fact]
    public async Task EntityForm_RunsOnACopy_StoredItemsStayUndeleted()
    {
        await EntityFormAcceptsAsync(AlphaMember, MemberAlpha);

        using var scope = scenario.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<IDataItemRepository>()
            .GetByIdAsync(scenario.DataItems[MemberAlpha].Id, default);
        Assert.False(stored!.IsDeleted);
    }

    // The use case loads the item, its policy and the requester's membership, and asks the entity
    private async Task<bool> EntityFormAcceptsAsync(string userKey, string itemKey)
    {
        using var scope = scenario.CreateScope();
        var provider = scope.ServiceProvider;
        var handler = new DeleteDataItemHandler(
            provider.GetRequiredService<IDataItemRepository>(),
            provider.GetRequiredService<IPolicyRepository>(),
            provider.GetRequiredService<IMembershipRepository>(),
            new NeverSavingUnitOfWork());

        var command = new DeleteDataItemCommand(scenario.DataItems[itemKey].Id, scenario.Users[userKey].Id);
        var result = await handler.Handle(command, default);
        if (result.IsFailure) Assert.Equal(DataItemErrors.NotFound, result.Error);
        return result.IsSuccess;
    }

    private async Task<HashSet<Guid>> DeletableIdsAsync(string userKey)
    {
        using var scope = scenario.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();

        var deletableIds = await db.DataItems.AsNoTracking()
            .DeletableBy(db, scenario.Users[userKey].Id)
            .Select(item => item.Id)
            .ToListAsync();

        return deletableIds.ToHashSet();
    }

    private sealed class NeverSavingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
