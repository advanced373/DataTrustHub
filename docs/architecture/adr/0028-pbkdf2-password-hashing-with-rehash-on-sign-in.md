# ADR-0028: Passwords are hashed with PBKDF2 (ASP.NET Core Identity) and re-hashed on sign-in

- **Status:** Accepted
- **Date:** 2026-11-04
- **Related:** #167; [ADR-0012](0012-clean-architecture-with-vertical-slices.md), [ADR-0025](0025-rate-limiting-and-audited-queries.md), [ADR-0024](0024-no-app-service-health-check-on-the-free-tier.md)

## Context

The `IPasswordHasher` adapter stored `Base64(SHA-256(password))`: no salt, one fast hash, and a plain
string comparison on verification. Two users with the same password got the same hash, and a leaked
`Users` table could be attacked with precomputed tables or billions of guesses per second on a GPU.

Constraints:

- The port lives in the Domain and must stay free of framework types (ADR-0012).
- Accounts already exist with SHA-256 hashes, and the plain passwords are unknown, so they cannot be
  converted in bulk.
- Production runs on the App Service **Free (F1)** plan (ADR-0024), with little CPU per sign-in.
- Sign-in must not reveal whether an account exists (ADR-0025).

## Decision

1. **Algorithm: PBKDF2 through ASP.NET Core Identity's `PasswordHasher<T>`** (package
   `Microsoft.Extensions.Identity.Core`), in its V3 format: PBKDF2-HMAC-SHA512, a random 128-bit salt
   per hash, and Identity's default iteration count (100 000), configurable through
   `PasswordHasherOptions`.
2. **The stored value describes itself.** The V3 format carries a version marker, the PRF, the
   iteration count and the salt length next to the salt and the subkey. Raising the iteration count
   later needs no migration: older hashes verify as "re-hash needed".
3. **The port reports re-hash.** `IPasswordHasher.VerifyHashedPassword` returns the Domain's
   `PasswordVerificationResult` (`Failed`, `Success`, `SuccessRehashNeeded`) instead of `bool`.
4. **Migration path: re-hash on the next successful sign-in.**
   - A legacy hash is recognised by its shape: Base64 of exactly 32 bytes (a bare SHA-256 digest; a V3
     hash is never 32 bytes). It is verified with a constant-time comparison and reports
     `SuccessRehashNeeded`.
   - On `SuccessRehashNeeded` the login use case hashes the password it just verified, replaces the
     user's password hash and commits once. Other sign-ins still do not commit.
   - Registration requests approved later carry their hash to the user, and are re-hashed the same way.
5. **Equal work for unknown accounts.** Login with an unknown email still runs one password hash, so the
   slower hash does not turn response time into a way to find out whether an account exists.
6. Hashes are never compared with `==`; only Identity's verification or `CryptographicOperations.FixedTimeEquals`.

## Alternatives considered

- **Argon2id.** The current OWASP first choice (memory-hard), but .NET has no built-in implementation:
  it needs a third-party native or managed package, and its memory cost is a risk on the Free plan.
  The port keeps the door open; the self-describing format and `SuccessRehashNeeded` make a later switch
  a re-hash-on-sign-in like this one.
- **bcrypt.** Also a third-party package, and it silently truncates passwords at 72 bytes.
- **Own PBKDF2 code (`Rfc2898DeriveBytes.Pbkdf2`) with an own format.** Same algorithm, but a format,
  versioning and parsing to design, test and maintain; Identity's is proven and already versioned.
- **Bulk migration, or forcing a password reset.** Impossible without the plain passwords, or a bad
  experience for every user. Wrapping each SHA-256 hash in PBKDF2 (hash of a hash) would protect all
  rows at once, but adds a third format to recognise for the life of the system; with few accounts,
  re-hash on sign-in is enough.
- **A higher iteration count now (OWASP suggests 210 000 for PBKDF2-HMAC-SHA512).** Doubles the CPU per
  sign-in on the Free plan. Can be raised later through `PasswordHasherOptions` without a migration.

## Consequences

**Easier:**
- Salted, slow, versioned hashes for every new password and every user who signs in again.
- Tuning the cost, or changing the algorithm, is a configuration or adapter change plus re-hash on sign-in.

**Harder:**
- Each sign-in, registration and account creation costs tens of milliseconds of CPU, by design.
- The login use case now writes when a re-hash is needed.
- Until a user with a legacy hash signs in, a wrong password for that account is checked with one fast
  SHA-256 hash, so it is answered faster than an unknown email. This is accepted for the migration
  period only; it ends as legacy hashes are replaced.
- Users who never sign in again keep a SHA-256 hash. Once the remaining legacy hashes are few, they can
  be forced to reset, and the legacy branch removed from the adapter (a follow-up, not part of #167).
