using FluentValidation;

namespace DataTrustHub.Application.User.Register
{
    public class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
    {
        public RegisterUserCommandValidator()
        {
            RuleFor(x => x.Email).NotEmpty().EmailAddress();
            RuleFor(x => x.HashedPassword).NotEmpty();
        }
    }
}
