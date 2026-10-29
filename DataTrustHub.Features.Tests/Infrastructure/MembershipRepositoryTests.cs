using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure;

// IMembershipRepository maps DbMembership rows to Membership entities and back (Mapperly); each read
// uses a fresh scope, so the assertions see what was really saved
public class MembershipRepositoryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string UnknownRoleName = "Owner";

    [Fact]
    public async Task Membership_RoundTripsIdsAndRole()
    {
        var membership = Membership.AddGuest(Guid.NewGuid(), Guid.NewGuid());
        await SaveAsync(membership);

        var loaded = await QueryAsync(repo => repo.GetByIdAsync(membership.Id, default));

        Assert.Equal((membership.Id, membership.UserId, membership.OrganizationId, MembershipRole.Guest),
            (loaded!.Id, loaded.UserId, loaded.OrganizationId, loaded.Role));
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        Assert.Null(await QueryAsync(repo => repo.GetByIdAsync(Guid.NewGuid(), default)));
    }

    [Fact]
    public async Task GetAsync_ReturnsTheUsersMembershipInThatOrganization()
    {
        var userId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var membership = Membership.AppointOrgAdmin(userId, organizationId);
        await SaveAsync(membership, Membership.AddMember(userId, Guid.NewGuid()), Membership.AddMember(Guid.NewGuid(), organizationId));

        var loaded = await QueryAsync(repo => repo.GetAsync(userId, organizationId, default));

        Assert.Equal((membership.Id, MembershipRole.OrgAdmin), (loaded!.Id, loaded.Role));
    }

    [Fact]
    public async Task GetAsync_NoMembershipInThatOrganization_ReturnsNull()
    {
        var userId = Guid.NewGuid();
        await SaveAsync(Membership.AddMember(userId, Guid.NewGuid()));

        Assert.Null(await QueryAsync(repo => repo.GetAsync(userId, Guid.NewGuid(), default)));
    }

    [Fact]
    public async Task ExistsAsync_TrueOnlyForTheSameUserAndOrganization()
    {
        var userId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        await SaveAsync(Membership.AddMember(userId, organizationId));

        Assert.True(await QueryAsync(repo => repo.ExistsAsync(userId, organizationId, default)));
        Assert.False(await QueryAsync(repo => repo.ExistsAsync(userId, Guid.NewGuid(), default)));
        Assert.False(await QueryAsync(repo => repo.ExistsAsync(Guid.NewGuid(), organizationId, default)));
    }

    [Fact]
    public async Task CountOrgAdminsAsync_CountsOnlyOrgAdminsOfThatOrganization()
    {
        var organizationId = Guid.NewGuid();
        await SaveAsync(
            Membership.AppointOrgAdmin(Guid.NewGuid(), organizationId),
            Membership.AppointOrgAdmin(Guid.NewGuid(), organizationId),
            Membership.AddMember(Guid.NewGuid(), organizationId),
            Membership.AddGuest(Guid.NewGuid(), organizationId),
            Membership.AppointOrgAdmin(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(2, await QueryAsync(repo => repo.CountOrgAdminsAsync(organizationId, default)));
    }

    [Fact]
    public async Task CountOrgAdminsAsync_OrganizationWithoutOrgAdmin_ReturnsZero()
    {
        var organizationId = Guid.NewGuid();
        await SaveAsync(Membership.AddMember(Guid.NewGuid(), organizationId));

        Assert.Equal(0, await QueryAsync(repo => repo.CountOrgAdminsAsync(organizationId, default)));
    }

    [Fact]
    public async Task ListForOrganizationAsync_ReturnsEveryMembershipOfThatOrganizationOnly()
    {
        var organizationId = Guid.NewGuid();
        var member = Membership.AddMember(Guid.NewGuid(), organizationId);
        var guest = Membership.AddGuest(Guid.NewGuid(), organizationId);
        await SaveAsync(member, guest, Membership.AddMember(Guid.NewGuid(), Guid.NewGuid()));

        var listed = await QueryAsync(repo => repo.ListForOrganizationAsync(organizationId, default));

        Assert.Equal(new[] { member.Id, guest.Id }.Order(), listed.Select(m => m.Id).Order());
        Assert.Equal(MembershipRole.Guest, listed.Single(m => m.Id == guest.Id).Role);
    }

    [Fact]
    public async Task ListForUserAsync_ReturnsEveryMembershipOfThatUserOnly()
    {
        var userId = Guid.NewGuid();
        var first = Membership.AddMember(userId, Guid.NewGuid());
        var second = Membership.AppointOrgAdmin(userId, Guid.NewGuid());
        await SaveAsync(first, second, Membership.AddMember(Guid.NewGuid(), first.OrganizationId));

        var listed = await QueryAsync(repo => repo.ListForUserAsync(userId, default));

        Assert.Equal(new[] { first.Id, second.Id }.Order(), listed.Select(m => m.Id).Order());
    }

    [Fact]
    public async Task ListForUserAsync_UserWithoutMemberships_ReturnsEmpty()
    {
        Assert.Empty(await QueryAsync(repo => repo.ListForUserAsync(Guid.NewGuid(), default)));
    }

    [Fact]
    public async Task UpdateAsync_PersistsARoleChange()
    {
        var membership = Membership.AddMember(Guid.NewGuid(), Guid.NewGuid());
        await SaveAsync(membership);

        await InScopeAsync(async (repo, uow) =>
        {
            var stored = (await repo.GetByIdAsync(membership.Id, default))!;
            Assert.True(stored.ChangeRole(MembershipRole.OrgAdmin).IsSuccess);
            await repo.UpdateAsync(stored, default);
            await uow.SaveChangesAsync(default);
        });

        var loaded = await QueryAsync(repo => repo.GetByIdAsync(membership.Id, default));
        Assert.Equal((membership.UserId, membership.OrganizationId, MembershipRole.OrgAdmin), (loaded!.UserId, loaded.OrganizationId, loaded.Role));
    }

    [Fact]
    public async Task RemoveAsync_DeletesOnlyThatMembership()
    {
        var userId = Guid.NewGuid();
        var removed = Membership.AddMember(userId, Guid.NewGuid());
        var kept = Membership.AddMember(userId, Guid.NewGuid());
        await SaveAsync(removed, kept);

        await InScopeAsync(async (repo, uow) => { await repo.RemoveAsync(removed, default); await uow.SaveChangesAsync(default); });

        Assert.Null(await QueryAsync(repo => repo.GetByIdAsync(removed.Id, default)));
        Assert.False(await QueryAsync(repo => repo.ExistsAsync(userId, removed.OrganizationId, default)));
        Assert.NotNull(await QueryAsync(repo => repo.GetByIdAsync(kept.Id, default)));
    }

    [Fact]
    public async Task Add_NothingIsStoredBeforeSaveChanges()
    {
        var membership = Membership.AddMember(Guid.NewGuid(), Guid.NewGuid());
        await InScopeAsync((repo, _) => { repo.Add(membership); return Task.CompletedTask; });

        Assert.Null(await QueryAsync(repo => repo.GetByIdAsync(membership.Id, default)));
    }

    [Fact]
    public async Task Role_IsStoredByName()
    {
        var membership = Membership.AppointOrgAdmin(Guid.NewGuid(), Guid.NewGuid());
        await SaveAsync(membership);

        using var scope = factory.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<DContext>().Memberships.SingleAsync(m => m.Id == membership.Id);
        Assert.Equal(MembershipRole.OrgAdmin.Name, row.Role);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownStoredRole_Throws()
    {
        var id = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DContext>();
            db.Memberships.Add(new DbMembership { Id = id, UserId = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), Role = UnknownRoleName });
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => QueryAsync(repo => repo.GetByIdAsync(id, default)));
    }

    [Fact]
    public void Repository_IsScoped()
    {
        using var first = factory.Services.CreateScope();
        using var second = factory.Services.CreateScope();

        Assert.Same(first.ServiceProvider.GetRequiredService<IMembershipRepository>(), first.ServiceProvider.GetRequiredService<IMembershipRepository>());
        Assert.NotSame(first.ServiceProvider.GetRequiredService<IMembershipRepository>(), second.ServiceProvider.GetRequiredService<IMembershipRepository>());
    }

    // --- helpers ---

    private Task SaveAsync(params Membership[] memberships) =>
        InScopeAsync(async (repo, uow) =>
        {
            foreach (var membership in memberships) repo.Add(membership);
            await uow.SaveChangesAsync(default);
        });

    private async Task InScopeAsync(Func<IMembershipRepository, IUnitOfWork, Task> work)
    {
        using var scope = factory.Services.CreateScope();
        await work(scope.ServiceProvider.GetRequiredService<IMembershipRepository>(), scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
    }

    private async Task<TResult> QueryAsync<TResult>(Func<IMembershipRepository, Task<TResult>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<IMembershipRepository>());
    }
}
