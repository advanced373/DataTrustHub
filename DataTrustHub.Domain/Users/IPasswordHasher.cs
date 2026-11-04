namespace DataTrustHub.Domain.Users
{
    public interface IPasswordHasher
    {
        public string HashPassword(string password);

        // Never throws for a malformed stored hash: that is Failed
        public PasswordVerificationResult VerifyHashedPassword(string hashedPassword, string providedPassword);
    }
}
