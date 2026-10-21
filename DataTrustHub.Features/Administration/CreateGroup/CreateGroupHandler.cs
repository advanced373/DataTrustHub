using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.CreateGroup;

public record CreateGroupCommand(string Name);

public class CreateGroupHandler(DContext db)
    : ICommandHandler<CreateGroupCommand, Result<Guid>>
{
    private readonly DContext _db = db;

    public async Task<Result<Guid>> Handle(
        CreateGroupCommand command,
        CancellationToken cancellationToken)
    {
        var nameExists = await CheckGroupNameExists(command.Name, cancellationToken);
        if (nameExists) return Result.Failure<Guid>(Errors.GroupNameAlreadyInUse);

        var groupId = Guid.NewGuid();
        await PersistGroup(groupId, command.Name, cancellationToken);
        return Result.Success(groupId);
    }

    private Task<bool> CheckGroupNameExists(string name, CancellationToken ct) =>
        _db.Organizations.AnyAsync(o => o.Name == name, ct);

    private async Task PersistGroup(Guid groupId, string name, CancellationToken ct)
    {
        var dbOrganization = new DbOrganization
        {
            Id = groupId,
            Name = name
        };
        await _db.Organizations.AddAsync(dbOrganization, ct);
        await _db.SaveChangesAsync(ct);
    }
}
