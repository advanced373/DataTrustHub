using DataTrustHub.Domain.ReadModels.Users;
using DataTrustHub.Features._Shared.Messaging;
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Administration.GetAccount;

public record GetAccountQuery(Guid AccountId);

public class GetAccountHandler(IUserQueries userQueries) : IQueryHandler<GetAccountQuery, Result<GetAccountResponse>>
{
    private readonly IUserQueries _userQueries = userQueries;

    public async Task<Result<GetAccountResponse>> Handle(
        GetAccountQuery query,
        CancellationToken cancellationToken)
    {
        var account = await _userQueries.FindAccountAsync(query.AccountId, cancellationToken);

        return account is null
            ? Result.Failure<GetAccountResponse>(Errors.AccountNotFound)
            : Result.Success(ToResponse(account));
    }

    private static GetAccountResponse ToResponse(AccountEntry account) =>
        new(account.Id, account.Email, account.Role);
}
