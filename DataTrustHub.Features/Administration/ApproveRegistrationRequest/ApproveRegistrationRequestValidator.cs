using FluentValidation;

namespace DataTrustHub.Features.Administration.ApproveRegistrationRequest;

internal class ApproveRegistrationRequestValidator : AbstractValidator<ApproveRegistrationRequestCommand>
{
    public ApproveRegistrationRequestValidator()
    {
        RuleFor(x => x.RegistrationRequestId).NotEmpty();
    }
}
