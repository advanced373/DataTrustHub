# Development setup

The committed `appsettings.json` files hold no secret and no machine-specific setting (ADR-0007,
ADR-0017). Each developer keeps them in .NET User Secrets of the API project; deployed environments
take them from the environment or Key Vault, never from committed files.

## Prerequisites

- .NET 10 SDK
- SQL Server, e.g. LocalDB (`(localdb)\MSSQLLocalDB`)

## User Secrets of the API

Run these from the repository root. The API refuses to start without a valid `Jwt:Secret` and
cannot reach a database without `ConnectionStrings:DefaultConnection`; the admin seed is optional
(without it no admin is seeded).

```bash
# Your local database (machine-specific)
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\MSSQLLocalDB;Database=datatrusthubdb;Trusted_Connection=True;TrustServerCertificate=True" --project DataTrustHub.API

# The JWT signing key: a random value of at least 32 bytes (HMAC-SHA256)
dotnet user-secrets set "Jwt:Secret" "$(openssl rand -base64 48)" --project DataTrustHub.API

# The first admin, seeded at startup (ADR-0006)
dotnet user-secrets set "AdminSeed:Email" "admin@example.com" --project DataTrustHub.API
dotnet user-secrets set "AdminSeed:Password" "<a strong password>" --project DataTrustHub.API
```

Without `openssl`, generate the JWT signing key in PowerShell:

```powershell
$key = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
dotnet user-secrets set "Jwt:Secret" $key --project DataTrustHub.API
```

Check what is set with `dotnet user-secrets list --project DataTrustHub.API`. Never paste the key
into a committed file, an issue or a pull request.

## Local database

```bash
dotnet ef database update --project DataTrustHub.Infrastructure --startup-project DataTrustHub.API
```

## Build and test

```bash
dotnet build DataTrustHub.sln
dotnet test DataTrustHub.Features.Tests/
```

The tests need no User Secrets: the test host supplies its own signing key and an in-memory database.
