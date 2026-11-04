using DataTrustHub.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using IdentityVerificationResult = Microsoft.AspNetCore.Identity.PasswordVerificationResult;
using PasswordVerificationResult = DataTrustHub.Domain.Users.PasswordVerificationResult;

namespace DataTrustHub.Infrastructure.Services
{
    // Hashes passwords with ASP.NET Core Identity's hasher: PBKDF2-HMAC-SHA512, a random salt, and a format (V3)
    // that carries its algorithm and iteration count. The iteration count comes from PasswordHasherOptions, so
    // a host can configure it. Hashes are compared in constant time only. Thread-safe, registered as a singleton
    public sealed class PasswordHasher : IPasswordHasher
    {
        private readonly PasswordHasher<User> _identityHasher;

        public PasswordHasher(IOptions<PasswordHasherOptions> options)
        {
            // Always the V3 format, whatever compatibility mode the host set; only the iteration count is taken
            var v3Options = new PasswordHasherOptions
            {
                CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
                IterationCount = options.Value.IterationCount
            };
            _identityHasher = new PasswordHasher<User>(Options.Create(v3Options));
        }

        // Identity's hasher does not use the user, so none is passed
        public string HashPassword(string password) =>
            _identityHasher.HashPassword(null!, password);

        // A malformed stored value is Failed, never an exception
        public PasswordVerificationResult VerifyHashedPassword(string hashedPassword, string providedPassword)
        {
            if (string.IsNullOrEmpty(hashedPassword)) return PasswordVerificationResult.Failed;

            // Decoded bytes are never more than the Base64 characters, so this buffer is always large enough
            var decoded = new byte[hashedPassword.Length];
            if (!Convert.TryFromBase64String(hashedPassword, decoded, out var decodedLength) || decodedLength == 0)
                return PasswordVerificationResult.Failed;

            // A bare SHA-256 digest is the legacy format; a V3 hash never decodes to 32 bytes
            if (decodedLength == SHA256.HashSizeInBytes)
                return VerifyLegacyHash(decoded.AsSpan(0, decodedLength), providedPassword);

            return Map(_identityHasher.VerifyHashedPassword(null!, hashedPassword, providedPassword));
        }

        // The unsalted SHA-256 hash stored before #167: a match still signs in, but the hash must be replaced
        private static PasswordVerificationResult VerifyLegacyHash(ReadOnlySpan<byte> storedDigest, string providedPassword)
        {
            var providedDigest = SHA256.HashData(Encoding.UTF8.GetBytes(providedPassword));
            return CryptographicOperations.FixedTimeEquals(storedDigest, providedDigest)
                ? PasswordVerificationResult.SuccessRehashNeeded
                : PasswordVerificationResult.Failed;
        }

        private static PasswordVerificationResult Map(IdentityVerificationResult result) => result switch
        {
            IdentityVerificationResult.Success => PasswordVerificationResult.Success,
            IdentityVerificationResult.SuccessRehashNeeded => PasswordVerificationResult.SuccessRehashNeeded,
            _ => PasswordVerificationResult.Failed
        };
    }
}
