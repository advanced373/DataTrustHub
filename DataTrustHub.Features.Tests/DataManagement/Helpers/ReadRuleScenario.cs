using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.DataItems;
using DataTrustHub.Domain.Organizations;
using DataTrustHub.Domain.Policies;
using DataTrustHub.Domain.Users;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DataTrustHub.Features.Tests.DataManagement.Helpers;

// One shared scenario for the read rule (#99), held twice: as domain entities built through the
// factory methods, and as persisted rows with the same ids in an in-memory SQLite database. SQLite
// is relational, so the SQL form of the read rule goes through real SQL translation and NULL semantics.
public sealed class ReadRuleScenario : IAsyncLifetime
{
    // Users, keyed by the clearances they hold
    public const string Owner = "owner";                                // Top Secret on Apollo, Secret on HR
    public const string ApolloSecretReader = "apollo-secret-reader";    // Secret on Apollo
    public const string InternalReader = "internal-reader";             // Internal on Apollo and on HR
    public const string HrTopSecretReader = "hr-top-secret-reader";     // Top Secret on HR only
    public const string Admin = "admin";                                // Admin role, no clearance
    public const string UnclearedUser = "uncleared-user";               // no clearance
    // Owners whose clearance changed after they uploaded (D3': the owner has no right on classified items)
    public const string RevokedOwner = "revoked-owner";                 // Secret on Apollo at upload, then revoked: none
    public const string DemotedOwner = "demoted-owner";                 // Secret on Apollo at upload, then Internal on Apollo
    public const string OtherPolicyOwner = "other-policy-owner";        // Secret on Apollo at upload, then Secret on HR only

    // Data items, owned by Owner unless the name says otherwise
    public const string OwnerPersonal = "owner-personal";
    public const string OwnerPersonalDeleted = "owner-personal-deleted";
    public const string ApolloPublic = "apollo-public";
    public const string ApolloConfidential = "apollo-confidential";
    public const string ApolloSecret = "apollo-secret";
    public const string ApolloSecretDeleted = "apollo-secret-deleted";
    public const string ApolloTopSecret = "apollo-top-secret";
    public const string HrInternal = "hr-internal";
    public const string HrSecretDeleted = "hr-secret-deleted";
    public const string ReaderPersonal = "apollo-secret-reader-personal";
    public const string ReaderApolloConfidential = "apollo-secret-reader-apollo-confidential";
    public const string AdminPersonal = "admin-personal";
    public const string RevokedOwnerPersonal = "revoked-owner-personal";
    public const string RevokedOwnerApolloConfidential = "revoked-owner-apollo-confidential";
    public const string DemotedOwnerApolloConfidential = "demoted-owner-apollo-confidential";
    public const string OtherPolicyOwnerApolloConfidential = "other-policy-owner-apollo-confidential";

    private const string ConnectionString = "DataSource=:memory:";
    private const string OrganizationName = "Read rule organization";
    private const string ApolloPolicyName = "Project Apollo";
    private const string HrPolicyName = "HR";
    private const string ClearanceName = "read rule clearance";
    private const string EmailDomain = "@example.com";
    private const string HashedPassword = "hash";
    private const string Content = "eA==";
    private const long Size = 1;

    private static readonly ClassificationLevel Public = ClassificationLevel.FindStandard(0)!;
    private static readonly ClassificationLevel Internal = ClassificationLevel.FindStandard(1)!;
    private static readonly ClassificationLevel Confidential = ClassificationLevel.FindStandard(2)!;
    private static readonly ClassificationLevel Secret = ClassificationLevel.FindStandard(3)!;
    private static readonly ClassificationLevel TopSecret = ClassificationLevel.FindStandard(4)!;

    private readonly SqliteConnection connection = new(ConnectionString);
    private readonly Dictionary<string, User> users = [];
    private readonly Dictionary<string, DataItem> dataItems = [];
    private readonly List<Clearance> clearances = [];
    private readonly Organization organization = Organization.Create(OrganizationName);
    private readonly Policy apollo;
    private readonly Policy hr;

    public ReadRuleScenario()
    {
        apollo = Policy.Create(ApolloPolicyName, organization.Id);
        hr = Policy.Create(HrPolicyName, organization.Id);
    }

    public IReadOnlyDictionary<string, User> Users => users;

    public IReadOnlyDictionary<string, DataItem> DataItems => dataItems;

    // Every clearance of every user; the read rule must ignore the ones held by someone else
    public IReadOnlyList<Clearance> AllClearances => clearances;

    public IReadOnlyList<Clearance> ClearancesOf(string userKey) =>
        clearances.Where(clearance => clearance.UserId == users[userKey].Id).ToList();

    // A fresh context on the shared open connection, so reads never see tracked seed rows
    public DContext CreateContext() =>
        new(new DbContextOptionsBuilder<DContext>().UseSqlite(connection).Options);

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        BuildUsers();
        BuildClearances();
        BuildDataItems();
        ChangeClearancesAfterUpload();

        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();
        Persist(db);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await connection.DisposeAsync();

    private void BuildUsers()
    {
        foreach (var key in new[] { Owner, ApolloSecretReader, InternalReader, HrTopSecretReader, UnclearedUser,
                     RevokedOwner, DemotedOwner, OtherPolicyOwner })
            users[key] = User.Register(key + EmailDomain, HashedPassword);

        users[Admin] = User.CreateAdmin(Admin + EmailDomain, HashedPassword);
    }

    private void BuildClearances()
    {
        Grant(Owner, apollo, TopSecret);
        Grant(Owner, hr, Secret);
        Grant(ApolloSecretReader, apollo, Secret);
        Grant(InternalReader, apollo, Internal);
        Grant(InternalReader, hr, Internal);
        Grant(HrTopSecretReader, hr, TopSecret);
        Grant(RevokedOwner, apollo, Secret);
        Grant(DemotedOwner, apollo, Secret);
        Grant(OtherPolicyOwner, apollo, Secret);
    }

    private void BuildDataItems()
    {
        AddPersonal(OwnerPersonal, Owner);
        AddPersonal(OwnerPersonalDeleted, Owner, deleted: true);
        AddClassified(ApolloPublic, Owner, apollo, Public);
        AddClassified(ApolloConfidential, Owner, apollo, Confidential);
        AddClassified(ApolloSecret, Owner, apollo, Secret);
        AddClassified(ApolloSecretDeleted, Owner, apollo, Secret, deleted: true);
        AddClassified(ApolloTopSecret, Owner, apollo, TopSecret);
        AddClassified(HrInternal, Owner, hr, Internal);
        AddClassified(HrSecretDeleted, Owner, hr, Secret, deleted: true);
        AddPersonal(ReaderPersonal, ApolloSecretReader);
        AddClassified(ReaderApolloConfidential, ApolloSecretReader, apollo, Confidential);
        AddPersonal(AdminPersonal, Admin);
        AddPersonal(RevokedOwnerPersonal, RevokedOwner);
        AddClassified(RevokedOwnerApolloConfidential, RevokedOwner, apollo, Confidential);
        AddClassified(DemotedOwnerApolloConfidential, DemotedOwner, apollo, Confidential);
        AddClassified(OtherPolicyOwnerApolloConfidential, OtherPolicyOwner, apollo, Confidential);
    }

    // Upload needs a clearance (D2), so these owners lose or change it only after their items exist.
    // Runs before Persist, so the entity data and the persisted rows hold the same clearances.
    private void ChangeClearancesAfterUpload()
    {
        Revoke(RevokedOwner);
        Revoke(DemotedOwner);
        Grant(DemotedOwner, apollo, Internal);
        Revoke(OtherPolicyOwner);
        Grant(OtherPolicyOwner, hr, Secret);
    }

    private void Revoke(string userKey) => clearances.RemoveAll(clearance => clearance.UserId == users[userKey].Id);

    private void Grant(string userKey, Policy policy, ClassificationLevel level) =>
        clearances.Add(Clearance.Grant(ClearanceName, users[userKey].Id, policy.Id, level));

    private void AddPersonal(string key, string ownerKey, bool deleted = false) =>
        Add(key, DataItem.CreatePersonal(key, Size, Content, users[ownerKey].Id), deleted);

    private void AddClassified(
        string key, string ownerKey, Policy policy, ClassificationLevel level, bool deleted = false)
    {
        var created = DataItem.CreateClassified(
            key, Size, Content, users[ownerKey].Id, policy.Id, level, ClearancesOf(ownerKey));
        if (created.IsFailure) throw new InvalidOperationException($"Scenario item '{key}' could not be classified");

        Add(key, created.Value, deleted);
    }

    private void Add(string key, DataItem item, bool deleted)
    {
        if (deleted && item.MarkDeleted(item.OwnerUserId, DeletionRights.WithoutOrganization).IsFailure)
            throw new InvalidOperationException($"Scenario item '{key}' could not be deleted");

        dataItems[key] = item;
    }

    // The persisted rows mirror the entities field by field, with the same ids
    private void Persist(DContext db)
    {
        var levelRows = ClassificationLevel.Standard.ToDictionary(
            level => level.Priority,
            level => new DbClassificationLevel { Name = level.Name, Priority = level.Priority });
        db.ClassificationLevels.AddRange(levelRows.Values);

        db.Organizations.Add(new DbOrganization { Id = organization.Id, Name = organization.Name });
        db.Policies.AddRange(new[] { apollo, hr }.Select(policy =>
            new DbPolicy { Id = policy.Id, Name = policy.Name, OrganizationId = policy.OrganizationId }));
        db.Users.AddRange(users.Values.Select(user =>
            new DbUser { Id = user.Id, Email = user.Email, HashedPassword = user.HashedPassword, Role = user.Role }));
        db.Clearances.AddRange(clearances.Select(clearance => new DbClearance
        {
            Id = clearance.Id,
            Name = clearance.Name,
            UserId = clearance.UserId,
            PolicyId = clearance.PolicyId,
            ClassificationLevel = levelRows[clearance.Level.Priority]
        }));
        db.DataItems.AddRange(dataItems.Values.Select(ToRow));
    }

    private static DbDataItem ToRow(DataItem item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        Size = item.Size,
        Content = item.Content,
        OwnerUserId = item.OwnerUserId,
        SecurityMarking = item.SecurityMarking,
        PolicyId = item.PolicyId,
        ClassificationPriority = item.ClassificationPriority,
        IsDeleted = item.IsDeleted
    };
}
