using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.GetAccount;

public record GetAccountQuery(Guid AccountId);

public class GetAccountHandler(DContext db) : IQueryHandler<GetAccountQuery, Result<GetAccountResponse>>
{
    private readonly DContext _db = db;

    public async Task<Result<GetAccountResponse>> Handle(
        GetAccountQuery query,
        CancellationToken cancellationToken)
    {
        var account = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == query.AccountId)
            .Select(u => new GetAccountResponse(u.Id, u.Email, u.Role))
            .FirstOrDefaultAsync(cancellationToken);

        return account is null
            ? Result.Failure<GetAccountResponse>(Errors.AccountNotFound)
            : Result.Success(account);
    }
}
