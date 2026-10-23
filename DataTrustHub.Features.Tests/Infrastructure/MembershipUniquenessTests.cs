using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure;

// Invariant M1 in the database (ADR-0014): at most one membership per (user, organization). EF InMemory
// does not enforce unique indexes, so these tests run the real repository on relational SQLite.
public class MembershipUniquenessTests(SqliteApiFactory factory) : IClassFixture<SqliteApiFactory>
{
    private const string UserIdColumn = nameof(DbMembership.UserId);
    private const string OrganizationIdColumn = nameof(DbMembership.OrganizationId);
    private const string RoleColumn = nameof(DbMembership.Role);
    private const int RoleMaxLength = 32;

    [Fact]
    public async Task SecondMembership_SameUserAndOrganization_IsRejectedOnSave()
    {
        var userId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        await SaveAsync(Membership.AddMember(userId, organizationId));

        await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(Membership.AddGuest(userId, organizationId)));
        Assert.Single(await QueryAsync(repo => repo.ListForUserAsync(userId, default)));
    }

    [Fact]
    public async Task TwoMemberships_SameUserAndOrganizationInOneSave_AreRejected()
    {
        var userId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            SaveAsync(Membership.AddMember(userId, organizationId), Membership.AppointOrgAdmin(userId, organizationId)));
        Assert.False(await QueryAsync(repo => repo.ExistsAsync(userId, organizationId, default)));
    }

    [Fact]
    public async Task Memberships_SameUserOtherOrganization_OrOtherUserSameOrganization_AreAccepted()
    {
        var userId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();

        await SaveAsync(
            Membership.AddMember(userId, organizationId),
            Membership.AddMember(userId, Guid.NewGuid()),
            Membership.AddMember(Guid.NewGuid(), organizationId));

        Assert.Equal(2, (await QueryAsync(repo => repo.ListForUserAsync(userId, default))).Count);
        Assert.Equal(2, (await QueryAsync(repo => repo.ListForOrganizationAsync(organizationId, default))).Count);
    }

    [Fact]
    public async Task CountOrgAdminsAsync_TranslatesToSql()
    {
        var organizationId = Guid.NewGuid();
        await SaveAsync(
            Membership.AppointOrgAdmin(Guid.NewGuid(), organizationId),
            Membership.AddMember(Guid.NewGuid(), organizationId),
            Membership.AddGuest(Guid.NewGuid(), organizationId));

        Assert.Equal(1, await QueryAsync(repo => repo.CountOrgAdminsAsync(organizationId, default)));
    }

    [Fact]
    public void Model_HasUniqueIndexOnUserIdAndOrganizationId()
    {
        var index = Assert.Single(MembershipEntityType().GetIndexes());

        Assert.True(index.IsUnique);
        Assert.Equal([UserIdColumn, OrganizationIdColumn], index.Properties.Select(p => p.Name));
    }

    [Fact]
    public void Model_HasNoForeignKeys()
    {
        Assert.Empty(MembershipEntityType().GetForeignKeys());
    }

    [Fact]
    public void Model_RoleIsRequiredWithMaxLength()
    {
        var role = MembershipEntityType().FindProperty(RoleColumn)!;

        Assert.False(role.IsNullable);
        Assert.Equal(RoleMaxLength, role.GetMaxLength());
    }

    // --- helpers ---

    private Microsoft.EntityFrameworkCore.Metadata.IEntityType MembershipEntityType()
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<DContext>().Model.FindEntityType(typeof(DbMembership))!;
    }

    private async Task SaveAsync(params Membership[] memberships)
    {
        using var scope = factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IMembershipRepository>();
        foreach (var membership in memberships) repo.Add(membership);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(default);
    }

    private async Task<TResult> QueryAsync<TResult>(Func<IMembershipRepository, Task<TResult>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<IMembershipRepository>());
    }
}
