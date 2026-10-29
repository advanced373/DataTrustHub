using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.ListAccounts;

public record ListAccountsQuery;

public class ListAccountsHandler(DContext db)
    : IQueryHandler<ListAccountsQuery, Result<IReadOnlyList<AccountSummary>>>
{
    private readonly DContext _db = db;

    // Password hashes never leave the handler: only the summary fields are projected
    public async Task<Result<IReadOnlyList<AccountSummary>>> Handle(
        ListAccountsQuery query,
        CancellationToken cancellationToken)
    {
        var accounts = await _db.Users
            .AsNoTracking()
            .OrderBy(u => u.Email)
            .Select(u => new AccountSummary(u.Id, u.Email, u.Role))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<AccountSummary>>(accounts);
    }
}
