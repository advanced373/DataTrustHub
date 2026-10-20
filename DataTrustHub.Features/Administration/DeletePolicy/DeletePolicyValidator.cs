using FluentValidation;

namespace DataTrustHub.Features.Administration.DeletePolicy;

internal class DeletePolicyValidator : AbstractValidator<DeletePolicyCommand>
{
    public DeletePolicyValidator()
    {
        RuleFor(x => x.PolicyId).NotEmpty();
    }
}
