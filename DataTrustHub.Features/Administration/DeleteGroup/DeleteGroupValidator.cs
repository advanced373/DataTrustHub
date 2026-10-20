using FluentValidation;

namespace DataTrustHub.Features.Administration.DeleteGroup;

internal class DeleteGroupValidator : AbstractValidator<DeleteGroupCommand>
{
    public DeleteGroupValidator()
    {
        RuleFor(x => x.GroupId).NotEmpty();
    }
}
