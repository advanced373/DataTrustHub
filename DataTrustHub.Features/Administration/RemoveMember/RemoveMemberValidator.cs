using FluentValidation;

namespace DataTrustHub.Features.Administration.RemoveMember;

internal class RemoveMemberValidator : AbstractValidator<RemoveMemberCommand>
{
    public RemoveMemberValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.CallerId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}
