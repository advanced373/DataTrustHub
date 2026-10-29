using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.DataItems;
using DataTrustHub.Domain.Memberships;
using DataTrustHub.Domain.Organizations;
using DataTrustHub.Domain.Policies;
using DataTrustHub.Domain.Users;

namespace DataTrustHub.Features.Tests.Administration.Helpers;

// In-memory fakes for handler-level tests: they record what a handler staged,
// and the unit of work counts how many times it was asked to commit

internal sealed class CountingUnitOfWork : IUnitOfWork
{
    internal int SaveChangesCalls { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCalls++;
        return Task.FromResult(0);
    }
}

// Simulates M1 under concurrency: another request saved the same membership first, so the unique
// index rejects this save, as DContext reports it
internal sealed class UniqueViolationUnitOfWork : IUnitOfWork
{
    internal int SaveChangesCalls { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCalls++;
        throw new UniqueConstraintViolationException(new InvalidOperationException());
    }
}

internal sealed class FakeMembershipRepository : IMembershipRepository
{
    internal List<Membership> Stored { get; } = [];
    internal List<Membership> Added { get; } = [];
    internal List<Membership> Updated { get; } = [];
    internal List<Membership> Removed { get; } = [];

    internal Membership Seed(Membership membership)
    {
        Stored.Add(membership);
        return membership;
    }

    public Task<Membership?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.SingleOrDefault(m => m.Id == id));

    public Task<Membership?> GetAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.SingleOrDefault(m => m.UserId == userId && m.OrganizationId == organizationId));

    public Task<bool> ExistsAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.Any(m => m.UserId == userId && m.OrganizationId == organizationId));

    public Task<int> CountOrgAdminsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.Count(m => m.OrganizationId == organizationId && m.Role.IsOrgAdmin));

    public Task<IReadOnlyList<Membership>> ListForOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Membership>>(Stored.Where(m => m.OrganizationId == organizationId).ToList());

    public Task<IReadOnlyList<Membership>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Membership>>(Stored.Where(m => m.UserId == userId).ToList());

    public void Add(Membership membership) => Added.Add(membership);

    public Task UpdateAsync(Membership membership, CancellationToken cancellationToken)
    {
        Updated.Add(membership);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Membership membership, CancellationToken cancellationToken)
    {
        Removed.Add(membership);
        return Task.CompletedTask;
    }
}

internal sealed class FakeOrganizationRepository : IOrganizationRepository
{
    internal List<Organization> Stored { get; } = [];
    internal List<Organization> Added { get; } = [];
    internal List<Organization> Removed { get; } = [];

    internal Organization Seed(string name)
    {
        var organization = Organization.Create(name);
        Stored.Add(organization);
        return organization;
    }

    public Task<Organization?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.SingleOrDefault(o => o.Id == id));

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.Any(o => o.Id == id));

    public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.Any(o => o.Name == name));

    public void Add(Organization organization) => Added.Add(organization);

    public Task RemoveAsync(Organization organization, CancellationToken cancellationToken)
    {
        Removed.Add(organization);
        return Task.CompletedTask;
    }
}

internal sealed class FakePolicyRepository : IPolicyRepository
{
    internal List<Policy> Stored { get; } = [];
    internal List<Policy> Added { get; } = [];
    internal List<Policy> Removed { get; } = [];

    internal Policy Seed(string name, Guid organizationId)
    {
        var policy = Policy.Create(name, organizationId);
        Stored.Add(policy);
        return policy;
    }

    public Task<Policy?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.SingleOrDefault(p => p.Id == id));

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.Any(p => p.Id == id));

    public Task<bool> NameExistsInOrganizationAsync(string name, Guid organizationId, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.Any(p => p.Name == name && p.OrganizationId == organizationId));

    public Task<bool> AnyInOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.Any(p => p.OrganizationId == organizationId));

    public Task<IReadOnlyList<Guid>> ListIdsInOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Stored.Where(p => p.OrganizationId == organizationId).Select(p => p.Id).ToList());

    public void Add(Policy policy) => Added.Add(policy);

    public Task RemoveAsync(Policy policy, CancellationToken cancellationToken)
    {
        Removed.Add(policy);
        return Task.CompletedTask;
    }
}

internal sealed class FakeClearanceRepository : IClearanceRepository
{
    // Marks a policy as having clearances without seeding a full clearance (policy delete checks)
    internal HashSet<Guid> PoliciesWithClearances { get; } = [];
    internal List<Clearance> Stored { get; } = [];
    internal List<Clearance> Added { get; } = [];
    internal List<Clearance> Removed { get; } = [];

    internal Clearance Seed(Guid userId, Guid policyId)
    {
        var clearance = Clearance.Grant("Seeded clearance", userId, policyId, new ClassificationLevel("Secret", 2));
        Stored.Add(clearance);
        return clearance;
    }

    public Task<bool> AnyForPolicyAsync(Guid policyId, CancellationToken cancellationToken) =>
        Task.FromResult(PoliciesWithClearances.Contains(policyId) || Stored.Any(c => c.PolicyId == policyId));

    public Task<Clearance?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.SingleOrDefault(c => c.Id == id));

    public Task<IReadOnlyList<Clearance>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Clearance>>(Stored.Where(c => c.UserId == userId).ToList());

    public Task<bool> ExistsAsync(Guid userId, Guid policyId, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.Any(c => c.UserId == userId && c.PolicyId == policyId));

    public Task AddAsync(Clearance clearance, CancellationToken cancellationToken)
    {
        Added.Add(clearance);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Clearance clearance, CancellationToken cancellationToken)
    {
        Removed.Add(clearance);
        return Task.CompletedTask;
    }
}

internal sealed class FakeUserRepository : IUserRepository
{
    internal List<User> Stored { get; } = [];
    internal List<User> Added { get; } = [];
    internal List<User> Removed { get; } = [];
    internal List<User> Updated { get; } = [];

    internal User Seed(string email) => Store(User.Register(email, FakePasswordHasher.HashPrefix + email));

    internal User SeedAdmin(string email) => Store(User.CreateAdmin(email, FakePasswordHasher.HashPrefix + email));

    private User Store(User user)
    {
        Stored.Add(user);
        return user;
    }

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.SingleOrDefault(u => u.Id == id));

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.SingleOrDefault(u => u.Email == EmailAddress.From(email).Value));

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.Any(u => u.Id == id));

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.Any(u => u.Email == EmailAddress.From(email).Value));

    public void Add(User user) => Added.Add(user);

    public Task UpdateAsync(User user, CancellationToken cancellationToken)
    {
        Updated.Add(user);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(User user, CancellationToken cancellationToken)
    {
        Removed.Add(user);
        return Task.CompletedTask;
    }
}

internal sealed class FakeDataItemRepository : IDataItemRepository
{
    internal List<DataItem> Stored { get; } = [];
    internal List<DataItem> Updated { get; } = [];

    internal DataItem Seed(Guid ownerUserId, bool isDeleted = false)
    {
        var item = DataItem.CreatePersonal("seeded.txt", 1, "AA==", ownerUserId);
        if (isDeleted) item.MarkDeleted(ownerUserId, DeletionRights.WithoutOrganization);
        Stored.Add(item);
        return item;
    }

    public Task<DataItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.SingleOrDefault(d => d.Id == id));

    public Task<IReadOnlyList<DataItem>> ListActiveOwnedByAsync(Guid ownerUserId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DataItem>>(
            Stored.Where(d => d.OwnerUserId == ownerUserId && !d.IsDeleted).ToList());

    public void Add(DataItem dataItem) => throw new NotSupportedException();

    public Task UpdateAsync(DataItem dataItem, CancellationToken cancellationToken)
    {
        Updated.Add(dataItem);
        return Task.CompletedTask;
    }
}

// A recognisable, reversible "hash" so tests can tell a hashed password from a plain one
internal sealed class FakePasswordHasher : IPasswordHasher
{
    internal const string HashPrefix = "hashed:";

    public string HashPassword(string password) => HashPrefix + password;

    public bool VerifyHashedPassword(string hashedPassword, string providedPassword) =>
        hashedPassword == HashPassword(providedPassword);
}
