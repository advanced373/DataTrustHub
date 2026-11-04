using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.Organizations;
using DataTrustHub.Domain.Policies;
using DataTrustHub.Domain.Users;
using DataTrustHub.Features.Tests.Infrastructure.Helpers;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.Infrastructure;

// #121 AC1-AC4 and AC7: the uniqueness rules U1, O1, P2 and C3 are unique indexes of the model, on columns that
// an index key can use (nvarchar(256), NOT NULL), and the M1 index on Memberships is unchanged. EF InMemory
// enforces no unique index, so the save tests run the real repositories on relational SQLite.
public class UniquenessIndexesModelTests(SqliteApiFactory factory) : IClassFixture<SqliteApiFactory>
{
    // The length every validator that writes these values allows (MaxEmailLength, MaxNameLength)
    private const int MaxLength = 256;
    private const string KeyColumnType = "nvarchar(256)";
    private const string MembershipIndexName = "IX_Memberships_UserId_OrganizationId";
    private const string LevelName = "Secret";
    private const int LevelPriority = 2;
    private const string PasswordHash = "hashed:unique";

    // Never opened: the SQL Server provider only builds the model the migrations are generated from
    private const string UnusedSqlServerConnection = "Server=unused;Database=unused;Trusted_Connection=True;";

    public static TheoryData<Type, string> KeyColumns => new()
    {
        { typeof(DbUser), nameof(DbUser.Email) },
        { typeof(DbOrganization), nameof(DbOrganization.Name) },
        { typeof(DbPolicy), nameof(DbPolicy.Name) }
    };

    public static TheoryData<Type, string[]> UniqueIndexes => new()
    {
        { typeof(DbUser), [nameof(DbUser.Email)] },
        { typeof(DbOrganization), [nameof(DbOrganization.Name)] },
        { typeof(DbPolicy), [nameof(DbPolicy.OrganizationId), nameof(DbPolicy.Name)] },
        { typeof(DbClearance), [nameof(DbClearance.UserId), nameof(DbClearance.PolicyId)] }
    };

    // --- the model (SQL Server, as the migrations see it) ---

    [Theory]
    [MemberData(nameof(KeyColumns))]
    public void Model_KeyColumn_IsNvarchar256AndRequired(Type entity, string property)
    {
        var column = SqlServerEntityType(entity).FindProperty(property)!;

        Assert.Equal((MaxLength, KeyColumnType, false), (column.GetMaxLength(), column.GetColumnType(), column.IsNullable));
    }

    [Theory]
    [MemberData(nameof(UniqueIndexes))]
    public void Model_HasTheUniqueIndexOfTheRule(Type entity, string[] columns)
    {
        var indexes = SqlServerEntityType(entity).GetIndexes();

        var index = Assert.Single(indexes, i => i.Properties.Select(p => p.Name).SequenceEqual(columns));
        Assert.True(index.IsUnique);
    }

    // A filtered unique index would leave some rows unconstrained
    [Theory]
    [MemberData(nameof(UniqueIndexes))]
    public void Model_UniqueIndexOfTheRule_HasNoFilter(Type entity, string[] columns)
    {
        var index = SqlServerEntityType(entity).GetIndexes().Single(i => i.Properties.Select(p => p.Name).SequenceEqual(columns));

        Assert.Null(index.GetFilter());
    }

    [Fact]
    public void Model_MembershipIndex_IsUnchanged()
    {
        var index = Assert.Single(SqlServerEntityType(typeof(DbMembership)).GetIndexes());

        Assert.True(index.IsUnique);
        Assert.Equal([nameof(DbMembership.UserId), nameof(DbMembership.OrganizationId)], index.Properties.Select(p => p.Name));
        Assert.Equal(MembershipIndexName, index.GetDatabaseName());
    }

    // AC7: the snapshot holds the same indexes and column types as the persistence models
    [Fact]
    public void Model_AgreesWithTheModelSnapshot()
    {
        using var context = SqlServerContext();
        var snapshot = context.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        var initialized = context.GetService<IModelRuntimeInitializer>().Initialize(snapshot, designTime: true, validationLogger: null);

        Assert.False(context.GetService<IMigrationsModelDiffer>().HasDifferences(
            initialized.GetRelationalModel(), context.GetService<IDesignTimeModel>().Model.GetRelationalModel()));
    }

    // --- the indexes enforced on save (relational SQLite) ---

    [Fact]
    public async Task SecondUser_SameEmail_IsRejectedOnSave()
    {
        var email = UniqueEmail();
        await SaveAsync<IUserRepository>(users => users.Add(User.Register(email, PasswordHash)));

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            SaveAsync<IUserRepository>(users => users.Add(User.Register(email, PasswordHash))));
        Assert.Equal(1, await CountAsync(db => db.Users.CountAsync(u => u.Email == email)));
    }

    [Fact]
    public async Task SecondOrganization_SameName_IsRejectedOnSave()
    {
        var name = UniqueName();
        await SaveAsync<IOrganizationRepository>(organizations => organizations.Add(Organization.Create(name)));

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            SaveAsync<IOrganizationRepository>(organizations => organizations.Add(Organization.Create(name))));
        Assert.Equal(1, await CountAsync(db => db.Organizations.CountAsync(o => o.Name == name)));
    }

    [Fact]
    public async Task SecondPolicy_SameNameSameOrganization_IsRejectedOnSave()
    {
        var name = UniqueName();
        var organizationId = Guid.NewGuid();
        await SaveAsync<IPolicyRepository>(policies => policies.Add(Policy.Create(name, organizationId)));

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            SaveAsync<IPolicyRepository>(policies => policies.Add(Policy.Create(name, organizationId))));
        Assert.Equal(1, await CountAsync(db => db.Policies.CountAsync(p => p.Name == name)));
    }

    // P2 is per organization: the same name in another organization is a different policy
    [Fact]
    public async Task Policies_SameNameOtherOrganization_AreAccepted()
    {
        var name = UniqueName();

        await SaveAsync<IPolicyRepository>(policies =>
        {
            policies.Add(Policy.Create(name, Guid.NewGuid()));
            policies.Add(Policy.Create(name, Guid.NewGuid()));
        });

        Assert.Equal(2, await CountAsync(db => db.Policies.CountAsync(p => p.Name == name)));
    }

    [Fact]
    public async Task SecondClearance_SameUserAndPolicy_IsRejectedOnSave()
    {
        var userId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        await SaveClearanceAsync(userId, policyId);

        await Assert.ThrowsAsync<DbUpdateException>(() => SaveClearanceAsync(userId, policyId));
        Assert.Equal(1, await CountAsync(db => db.Clearances.CountAsync(c => c.UserId == userId)));
    }

    // C3 is per (user, policy): the same user on another policy, or another user on the same policy, is fine
    [Fact]
    public async Task Clearances_SameUserOtherPolicy_OrOtherUserSamePolicy_AreAccepted()
    {
        var userId = Guid.NewGuid();
        var policyId = Guid.NewGuid();

        await SaveClearanceAsync(userId, policyId);
        await SaveClearanceAsync(userId, Guid.NewGuid());
        await SaveClearanceAsync(Guid.NewGuid(), policyId);

        Assert.Equal(2, await CountAsync(db => db.Clearances.CountAsync(c => c.UserId == userId)));
        Assert.Equal(2, await CountAsync(db => db.Clearances.CountAsync(c => c.PolicyId == policyId)));
    }

    // --- helpers ---

    private static string UniqueEmail() => $"unique_{Guid.NewGuid():N}@example.com";

    private static string UniqueName() => $"Unique {Guid.NewGuid():N}";

    private static DContext SqlServerContext() =>
        new(new DbContextOptionsBuilder<DContext>().UseSqlServer(UnusedSqlServerConnection).Options);

    private static IEntityType SqlServerEntityType(Type entity)
    {
        using var context = SqlServerContext();
        return context.GetService<IDesignTimeModel>().Model.FindEntityType(entity)!;
    }

    private Task SaveClearanceAsync(Guid userId, Guid policyId) =>
        SaveAsync<IClearanceRepository>(clearances => clearances.AddAsync(
            Clearance.Grant(UniqueName(), userId, policyId, new ClassificationLevel(LevelName, LevelPriority)), default));

    private Task SaveAsync<TRepository>(Action<TRepository> stage) where TRepository : notnull =>
        SaveAsync<TRepository>(repository =>
        {
            stage(repository);
            return Task.CompletedTask;
        });

    private async Task SaveAsync<TRepository>(Func<TRepository, Task> stage) where TRepository : notnull
    {
        using var scope = factory.Services.CreateScope();
        await stage(scope.ServiceProvider.GetRequiredService<TRepository>());
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(default);
    }

    private async Task<int> CountAsync(Func<DContext, Task<int>> count)
    {
        using var scope = factory.Services.CreateScope();
        return await count(scope.ServiceProvider.GetRequiredService<DContext>());
    }
}
