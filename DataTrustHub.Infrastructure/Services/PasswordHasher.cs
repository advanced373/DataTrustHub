using DataTrustHub.Domain.User;
using System.Security.Cryptography;
using System.Text;

namespace DataTrustHub.Infrastructure.Services
{
    public class PasswordHasher : IPasswordHasher
    {
        public string HashPassword(string password)
        {
            return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
        }

        public bool VerifyHashedPassword(string hashedPassword, string providedPassword)
        {
            var providedHashed = HashPassword(providedPassword);
            return hashedPassword == providedHashed;
        }
    }
}
