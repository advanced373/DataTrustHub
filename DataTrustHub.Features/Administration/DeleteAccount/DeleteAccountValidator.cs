using FluentValidation;

namespace DataTrustHub.Features.Administration.DeleteAccount;

internal class DeleteAccountValidator : AbstractValidator<DeleteAccountCommand>
{
    public DeleteAccountValidator()
    {
        RuleFor(x => x.AccountId).NotEmpty();
        RuleFor(x => x.RequesterId).NotEmpty();
    }
}
