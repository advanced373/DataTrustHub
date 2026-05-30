using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.CreatePolicy;

public record CreatePolicyCommand(string Name, Guid OrganizationId) : IRequest<Result<Guid>>;

public class CreatePolicyHandler(DContext db)
    : IRequestHandler<CreatePolicyCommand, Result<Guid>>
{
    private readonly DContext _db = db;

    public async Task<Result<Guid>> Handle(
        CreatePolicyCommand command,
        CancellationToken cancellationToken)
    {
        var organizationExists = await CheckOrganizationExists(command.OrganizationId, cancellationToken);
        if (!organizationExists) return Result.Failure<Guid>(Errors.OrganizationNotFound);

        var nameExists = await CheckPolicyNameExists(command.Name, command.OrganizationId, cancellationToken);
        if (nameExists) return Result.Failure<Guid>(Errors.PolicyNameAlreadyInUse);

        var policyId = Guid.NewGuid();
        await PersistPolicy(policyId, command.Name, command.OrganizationId, cancellationToken);
        return Result.Success(policyId);
    }

    private Task<bool> CheckOrganizationExists(Guid organizationId, CancellationToken ct) =>
        _db.Organizations.AnyAsync(o => o.Id == organizationId, ct);

    private Task<bool> CheckPolicyNameExists(string name, Guid organizationId, CancellationToken ct) =>
        _db.Policies.AnyAsync(p => p.Name == name && p.OrganizationId == organizationId, ct);

    private async Task PersistPolicy(
        Guid policyId, string name, Guid organizationId, CancellationToken ct)
    {
        var dbPolicy = new DbPolicy
        {
            Id = policyId,
            Name = name,
            OrganizationId = organizationId
        };
        await _db.Policies.AddAsync(dbPolicy, ct);
        await _db.SaveChangesAsync(ct);
    }
}
