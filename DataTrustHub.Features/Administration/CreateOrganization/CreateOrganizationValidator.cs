using FluentValidation;

namespace DataTrustHub.Features.Administration.CreateOrganization;

internal class CreateOrganizationValidator : AbstractValidator<CreateOrganizationCommand>
{
    public CreateOrganizationValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(Constants.MaxNameLength);
    }
}
