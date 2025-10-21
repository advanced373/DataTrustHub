namespace DataTrustHub.Application.User.Register
{
    public record RegisterUserCommand(string Email, string HashedPassword);
}
