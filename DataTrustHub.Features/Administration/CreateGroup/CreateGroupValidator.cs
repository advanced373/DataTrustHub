using FluentValidation;

namespace DataTrustHub.Features.Administration.CreateGroup;

internal class CreateGroupValidator : AbstractValidator<CreateGroupCommand>
{
    public CreateGroupValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(Constants.MaxNameLength);
    }
}
