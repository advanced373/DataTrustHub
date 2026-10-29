using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.DataItems;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.Organizations;
using DataTrustHub.Domain.Policies;
using DataTrustHub.Domain.Users;
using DataTrustHub.Infrastructure;
using DataTrustHub.Infrastructure.Persistance;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataTrustHub.Features.Tests.DataManagement.Helpers;

// One shared scenario for the delete rule D5' (#95 step 4b): two organizations with members of every role,
// a former member, an outsider, and a classified item whose policy no longer exists. It is stored through the
// real repositories on an in-memory SQLite database, so both forms of the rule run on the same rows: the
// use case form (DeleteDataItemHandler -> DataItem.MarkDeleted) and the SQL form (DataAccess.DeletableBy).
public sealed class DeleteRuleScenario : IAsyncLifetime
{
    // Users, keyed by their memberships
    public const string AlphaMember = "alpha-member";               // Member of Alpha
    public const string AlphaGuest = "alpha-guest";                 // Guest of Alpha
    public const string AlphaOrgAdmin = "alpha-org-admin";          // OrgAdmin of Alpha
    public const string BetaOrgAdmin = "beta-org-admin";            // OrgAdmin of Beta only
    public const string AlphaMemberBetaOrgAdmin = "two-org-user";   // Member of Alpha and OrgAdmin of Beta
    public const string FormerMember = "former-member";             // was a Member of Alpha, left after uploading
    public const string Outsider = "outsider";                      // no membership at all

    // Data items, keyed by owner and kind
    public const string MemberPersonal = "alpha-member-personal";
    public const string MemberPersonalDeleted = "alpha-member-personal-deleted";
    public const string MemberAlpha = "alpha-member-alpha";
    public const string MemberAlphaDeleted = "alpha-member-alpha-deleted";
    public const string MemberGonePolicy = "alpha-member-gone-policy";
    public const string GuestAlpha = "alpha-guest-alpha";
    public const string OrgAdminAlpha = "alpha-org-admin-alpha";
    public const string OrgAdminPersonal = "alpha-org-admin-personal";
    public const string BetaOrgAdminBeta = "beta-org-admin-beta";
    public const string TwoOrgUserAlpha = "two-org-user-alpha";
    public const string TwoOrgUserBeta = "two-org-user-beta";
    public const string FormerMemberAlpha = "former-member-alpha";
    public const string FormerMemberPersonal = "former-member-personal";
    public const string OutsiderPersonal = "outsider-personal";
    public const string OutsiderGonePolicy = "outsider-gone-policy";

    private const string ConnectionString = "DataSource=:memory:";
    private const string AlphaName = "Alpha";
    private const string BetaName = "Beta";
    private const string AlphaPolicyName = "Alpha policy";
    private const string BetaPolicyName = "Beta policy";
    private const string GonePolicyName = "Gone policy";
    private const string ClearanceName = "upload clearance";
    private const string EmailDomain = "@example.com";
    private const string HashedPassword = "hash";
    private const string Content = "eA==";
    private const long Size = 1;

    private static readonly ClassificationLevel Confidential = ClassificationLevel.FindStandard(2)!;

    private readonly SqliteConnection connection = new(ConnectionString);
    private readonly Dictionary<string, User> users = [];
    private readonly Dictionary<string, DataItem> dataItems = [];
    private readonly List<Membership> memberships = [];
    private readonly Organization alpha = Organization.Create(AlphaName);
    private readonly Organization beta = Organization.Create(BetaName);
    private readonly Policy alphaPolicy;
    private readonly Policy betaPolicy;

    // A policy of Alpha that is never stored: its items stand for "deleting a policy ignores its data items"
    private readonly Policy gonePolicy;

    private ServiceProvider services = null!;

    public DeleteRuleScenario()
    {
        alphaPolicy = Policy.Create(AlphaPolicyName, alpha.Id);
        betaPolicy = Policy.Create(BetaPolicyName, beta.Id);
        gonePolicy = Policy.Create(GonePolicyName, alpha.Id);
    }

    public IReadOnlyDictionary<string, User> Users => users;

    public IReadOnlyDictionary<string, DataItem> DataItems => dataItems;

    // A new scope on the shared SQLite connection, with the real repositories (ADR-0012)
    public IServiceScope CreateScope() => services.CreateScope();

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        services = BuildServices();
        BuildUsers();
        BuildMemberships();
        BuildDataItems();
        // The former member leaves after uploading; their item stays
        memberships.RemoveAll(membership => membership.UserId == users[FormerMember].Id);

        using var scope = CreateScope();
        await scope.ServiceProvider.GetRequiredService<DContext>().Database.EnsureCreatedAsync();
        Persist(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await services.DisposeAsync();
        await connection.DisposeAsync();
    }

    // The production registrations (AddInfrastructure), with SQLite in place of SQL Server
    private ServiceProvider BuildServices()
    {
        var collection = new ServiceCollection();
        collection.AddInfrastructure(new ConfigurationBuilder().Build());

        var sqlServerRegistrations = collection
            .Where(d => d.ServiceType == typeof(DbContextOptions<DContext>)
                || d.ServiceType == typeof(DbContextOptions)
                || d.ServiceType == typeof(DContext)
                || d.ServiceType == typeof(IDbContextOptionsConfiguration<DContext>))
            .ToList();
        foreach (var descriptor in sqlServerRegistrations) collection.Remove(descriptor);

        collection.AddDbContext<DContext>(options => options.UseSqlite(connection));
        return collection.BuildServiceProvider();
    }

    private void BuildUsers()
    {
        foreach (var key in new[]
                 {
                     AlphaMember, AlphaGuest, AlphaOrgAdmin, BetaOrgAdmin, AlphaMemberBetaOrgAdmin, FormerMember, Outsider
                 })
            users[key] = User.Register(key + EmailDomain, HashedPassword);
    }

    private void BuildMemberships()
    {
        memberships.Add(Membership.AddMember(users[AlphaMember].Id, alpha.Id));
        memberships.Add(Membership.AddGuest(users[AlphaGuest].Id, alpha.Id));
        memberships.Add(Membership.AppointOrgAdmin(users[AlphaOrgAdmin].Id, alpha.Id));
        memberships.Add(Membership.AppointOrgAdmin(users[BetaOrgAdmin].Id, beta.Id));
        memberships.Add(Membership.AddMember(users[AlphaMemberBetaOrgAdmin].Id, alpha.Id));
        memberships.Add(Membership.AppointOrgAdmin(users[AlphaMemberBetaOrgAdmin].Id, beta.Id));
        memberships.Add(Membership.AddMember(users[FormerMember].Id, alpha.Id));
    }

    private void BuildDataItems()
    {
        AddPersonal(MemberPersonal, AlphaMember);
        AddPersonal(MemberPersonalDeleted, AlphaMember, deleted: true);
        AddClassified(MemberAlpha, AlphaMember, alphaPolicy);
        AddClassified(MemberAlphaDeleted, AlphaMember, alphaPolicy, deleted: true);
        AddClassified(MemberGonePolicy, AlphaMember, gonePolicy);
        AddClassified(GuestAlpha, AlphaGuest, alphaPolicy);
        AddClassified(OrgAdminAlpha, AlphaOrgAdmin, alphaPolicy);
        AddPersonal(OrgAdminPersonal, AlphaOrgAdmin);
        AddClassified(BetaOrgAdminBeta, BetaOrgAdmin, betaPolicy);
        AddClassified(TwoOrgUserAlpha, AlphaMemberBetaOrgAdmin, alphaPolicy);
        AddClassified(TwoOrgUserBeta, AlphaMemberBetaOrgAdmin, betaPolicy);
        AddClassified(FormerMemberAlpha, FormerMember, alphaPolicy);
        AddPersonal(FormerMemberPersonal, FormerMember);
        AddPersonal(OutsiderPersonal, Outsider);
        AddClassified(OutsiderGonePolicy, Outsider, gonePolicy);
    }

    private void AddPersonal(string key, string ownerKey, bool deleted = false) =>
        Add(key, DataItem.CreatePersonal(key, Size, Content, users[ownerKey].Id), deleted);

    // The delete rule ignores clearances, so the upload clearance is not stored: no one here can read these items
    private void AddClassified(string key, string ownerKey, Policy policy, bool deleted = false)
    {
        var ownerId = users[ownerKey].Id;
        var uploadClearance = Clearance.Grant(ClearanceName, ownerId, policy.Id, Confidential);
        var created = DataItem.CreateClassified(key, Size, Content, ownerId, policy.Id, Confidential, [uploadClearance]);
        if (created.IsFailure) throw new InvalidOperationException($"Scenario item '{key}' could not be classified");

        Add(key, created.Value, deleted);
    }

    private void Add(string key, DataItem item, bool deleted)
    {
        if (deleted && item.MarkDeleted(item.OwnerUserId, DeletionRights.WithoutOrganization).IsFailure)
            throw new InvalidOperationException($"Scenario item '{key}' could not be deleted");

        dataItems[key] = item;
    }

    private void Persist(IServiceProvider provider)
    {
        var organizationRepository = provider.GetRequiredService<IOrganizationRepository>();
        organizationRepository.Add(alpha);
        organizationRepository.Add(beta);
        provider.GetRequiredService<IPolicyRepository>().Add(alphaPolicy);
        provider.GetRequiredService<IPolicyRepository>().Add(betaPolicy);
        foreach (var user in users.Values) provider.GetRequiredService<IUserRepository>().Add(user);
        foreach (var membership in memberships) provider.GetRequiredService<IMembershipRepository>().Add(membership);
        foreach (var item in dataItems.Values) provider.GetRequiredService<IDataItemRepository>().Add(item);
    }
}
