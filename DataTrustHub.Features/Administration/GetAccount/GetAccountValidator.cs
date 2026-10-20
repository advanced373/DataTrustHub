using FluentValidation;

namespace DataTrustHub.Features.Administration.GetAccount;

internal class GetAccountValidator : AbstractValidator<GetAccountQuery>
{
    public GetAccountValidator()
    {
        RuleFor(x => x.AccountId).NotEmpty();
    }
}
