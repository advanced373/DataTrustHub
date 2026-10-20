using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Administration.ListAccounts;

public record ListAccountsQuery : IRequest<Result<IReadOnlyList<AccountSummary>>>;

public class ListAccountsHandler(DContext db)
    : IRequestHandler<ListAccountsQuery, Result<IReadOnlyList<AccountSummary>>>
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
