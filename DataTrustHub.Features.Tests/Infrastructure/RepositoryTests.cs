using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.DataItems;
using DataTrustHub.Domain.Organizations;
using DataTrustHub.Domain.Policies;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features.Tests.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure;

// Repositories map Db* rows to domain entities and back (Mapperly); each read uses a fresh scope,
// so the assertions see what was really saved
public class RepositoryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task User_RoundTripsAndUpdatesAndRemoves()
    {
        var user = User.Register("repo-user@example.com", "hash");
        await InScopeAsync<IUserRepository>(async (repo, uow) => { repo.Add(user); await uow.SaveChangesAsync(default); });

        var loaded = await InScopeAsync<IUserRepository, User?>(repo => repo.GetByEmailAsync(user.Email, default));
        Assert.Equal((user.Id, user.Email, "hash", UserRoles.User), (loaded!.Id, loaded.Email, loaded.HashedPassword, loaded.Role));

        await InScopeAsync<IUserRepository>(async (repo, uow) =>
        {
            var stored = (await repo.GetByIdAsync(user.Id, default))!;
            stored.PromoteToAdmin();
            await repo.UpdateAsync(stored, default);
            await uow.SaveChangesAsync(default);
        });
        Assert.True((await InScopeAsync<IUserRepository, User?>(repo => repo.GetByIdAsync(user.Id, default)))!.IsAdmin);

        await InScopeAsync<IUserRepository>(async (repo, uow) => { await repo.RemoveAsync(user, default); await uow.SaveChangesAsync(default); });
        Assert.False(await InScopeAsync<IUserRepository, bool>(repo => repo.ExistsAsync(user.Id, default)));
    }

    [Fact]
    public async Task OrganizationAndPolicy_RoundTripWithExistenceChecks()
    {
        var organization = Organization.Create("Repo organization");
        var policy = Policy.Create("Repo policy", organization.Id);
        await InScopeAsync<IOrganizationRepository>(async (repo, uow) => { repo.Add(organization); await uow.SaveChangesAsync(default); });
        await InScopeAsync<IPolicyRepository>(async (repo, uow) => { repo.Add(policy); await uow.SaveChangesAsync(default); });

        var loaded = await InScopeAsync<IPolicyRepository, Policy?>(repo => repo.GetByIdAsync(policy.Id, default));
        Assert.Equal((policy.Name, organization.Id), (loaded!.Name, loaded.OrganizationId));
        Assert.True(await InScopeAsync<IOrganizationRepository, bool>(repo => repo.NameExistsAsync("Repo organization", default)));
        Assert.True(await InScopeAsync<IPolicyRepository, bool>(repo => repo.AnyInOrganizationAsync(organization.Id, default)));
        Assert.True(await InScopeAsync<IPolicyRepository, bool>(repo => repo.NameExistsInOrganizationAsync("Repo policy", organization.Id, default)));
    }

    [Fact]
    public async Task Clearance_ReusesLevelRow_MapsLevel_AndRemovalKeepsLevel()
    {
        var userId = Guid.NewGuid();
        var level = new ClassificationLevel("Repo level", 42);
        var first = Clearance.Grant("first", userId, Guid.NewGuid(), level);
        var second = Clearance.Grant("second", userId, Guid.NewGuid(), level);
        await InScopeAsync<IClearanceRepository>(async (repo, uow) => { await repo.AddAsync(first, default); await uow.SaveChangesAsync(default); });
        await InScopeAsync<IClearanceRepository>(async (repo, uow) => { await repo.AddAsync(second, default); await uow.SaveChangesAsync(default); });

        var listed = await InScopeAsync<IClearanceRepository, IReadOnlyList<Clearance>>(repo => repo.ListForUserAsync(userId, default));
        Assert.Equal(2, listed.Count);
        Assert.All(listed, c => Assert.Equal(level, c.Level));
        Assert.Equal(1, await CountLevelsAsync(level));

        await InScopeAsync<IClearanceRepository>(async (repo, uow) => { await repo.RemoveAsync(first, default); await uow.SaveChangesAsync(default); });
        Assert.Single(await InScopeAsync<IClearanceRepository, IReadOnlyList<Clearance>>(repo => repo.ListForUserAsync(userId, default)));
        Assert.Equal(1, await CountLevelsAsync(level));
    }

    [Fact]
    public async Task DataItem_RoundTripsClassification_AndPersistsSoftDelete()
    {
        var owner = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var secret = ClassificationLevel.FindStandard(3)!;
        var item = DataItem.CreateClassified("r.txt", 3, "YWJj", owner, policyId, secret, [Clearance.Grant("c", owner, policyId, secret)]).Value;
        await InScopeAsync<IDataItemRepository>(async (repo, uow) => { repo.Add(item); await uow.SaveChangesAsync(default); });

        var loaded = (await InScopeAsync<IDataItemRepository, DataItem?>(repo => repo.GetByIdAsync(item.Id, default)))!;
        Assert.Equal((item.Name, item.Size, item.Content, owner, "Secret", policyId, 3, false),
            (loaded.Name, loaded.Size, loaded.Content, loaded.OwnerUserId, loaded.SecurityMarking, loaded.PolicyId, loaded.ClassificationPriority, loaded.IsDeleted));

        await InScopeAsync<IDataItemRepository>(async (repo, uow) =>
        {
            var stored = (await repo.GetByIdAsync(item.Id, default))!;
            stored.MarkDeleted(owner);
            await repo.UpdateAsync(stored, default);
            await uow.SaveChangesAsync(default);
        });
        Assert.True((await InScopeAsync<IDataItemRepository, DataItem?>(repo => repo.GetByIdAsync(item.Id, default)))!.IsDeleted);
        Assert.Empty(await InScopeAsync<IDataItemRepository, IReadOnlyList<DataItem>>(repo => repo.ListActiveOwnedByAsync(owner, default)));
    }

    [Fact]
    public async Task UnitOfWork_NothingIsStoredBeforeSaveChanges()
    {
        var organization = Organization.Create("Unsaved organization");
        await InScopeAsync<IOrganizationRepository>((repo, _) => { repo.Add(organization); return Task.CompletedTask; });

        Assert.False(await InScopeAsync<IOrganizationRepository, bool>(repo => repo.ExistsAsync(organization.Id, default)));
    }

    // --- helpers ---

    private async Task InScopeAsync<TRepository>(Func<TRepository, IUnitOfWork, Task> work)
        where TRepository : notnull
    {
        using var scope = factory.Services.CreateScope();
        await work(scope.ServiceProvider.GetRequiredService<TRepository>(), scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
    }

    private async Task<TResult> InScopeAsync<TRepository, TResult>(Func<TRepository, Task<TResult>> query)
        where TRepository : notnull
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<TRepository>());
    }

    private async Task<int> CountLevelsAsync(ClassificationLevel level)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DContext>();
        return await db.ClassificationLevels.CountAsync(l => l.Name == level.Name && l.Priority == level.Priority);
    }
}
