using DataTrustHub.Domain.ReadModels.Users;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.ListAccounts;

public record ListAccountsQuery;

public class ListAccountsHandler(IUserQueries userQueries)
    : IQueryHandler<ListAccountsQuery, Result<IReadOnlyList<AccountSummary>>>
{
    private readonly IUserQueries _userQueries = userQueries;

    // Password hashes never leave the read side: the read model carries only the summary fields
    public async Task<Result<IReadOnlyList<AccountSummary>>> Handle(
        ListAccountsQuery query,
        CancellationToken cancellationToken)
    {
        var accounts = await _userQueries.ListAccountsAsync(cancellationToken);
        return Result.Success<IReadOnlyList<AccountSummary>>(accounts.Select(ToSummary).ToList());
    }

    private static AccountSummary ToSummary(AccountEntry account) =>
        new(account.Id, account.Email, account.Role);
}
