using DataTrustHub.Domain.Abstractions;
using DataTrustHub.Domain.Clearances;
using DataTrustHub.Domain.Organizations;
using DataTrustHub.Domain.Policies;

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

    public void Add(Policy policy) => Added.Add(policy);

    public Task RemoveAsync(Policy policy, CancellationToken cancellationToken)
    {
        Removed.Add(policy);
        return Task.CompletedTask;
    }
}

// Only the policy check is used by the handlers under test; the rest is out of scope for these fakes
internal sealed class FakeClearanceRepository : IClearanceRepository
{
    internal HashSet<Guid> PoliciesWithClearances { get; } = [];

    public Task<bool> AnyForPolicyAsync(Guid policyId, CancellationToken cancellationToken) =>
        Task.FromResult(PoliciesWithClearances.Contains(policyId));

    public Task<Clearance?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<Clearance>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<bool> ExistsAsync(Guid userId, Guid policyId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task AddAsync(Clearance clearance, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task RemoveAsync(Clearance clearance, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
