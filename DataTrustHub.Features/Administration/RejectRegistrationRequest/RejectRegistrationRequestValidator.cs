using FluentValidation;

namespace DataTrustHub.Features.Administration.RejectRegistrationRequest;

internal class RejectRegistrationRequestValidator : AbstractValidator<RejectRegistrationRequestCommand>
{
    public RejectRegistrationRequestValidator()
    {
        RuleFor(x => x.RegistrationRequestId).NotEmpty();
    }
}
