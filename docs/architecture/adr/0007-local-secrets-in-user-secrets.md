# ADR-0007: Local configuration and secrets in User Secrets

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** #40

## Context

The repository is public. Machine-specific settings (connection string to a named SQL Server)
and secrets were kept in `appsettings.json`, and they do not work on another developer's machine.

## Decision

- Secrets and machine-specific settings go in .NET User Secrets (`dotnet user-secrets --project DataTrustHub.API`):
  `ConnectionStrings:DefaultConnection`, `AdminSeed:Email`, `AdminSeed:Password`.
- Each developer uses a local database, e.g. `(localdb)\MSSQLLocalDB` / `datatrusthubdb`, created with
  `dotnet ef database update --project DataTrustHub.Infrastructure --startup-project DataTrustHub.API`.
- Deployed environments use environment variables or a secret store, never committed files.

## Alternatives considered

- **Per-developer `appsettings.*.json` committed to git.** Leaks secrets in a public repo.
- **Shared development database.** Developers' data and migrations would interfere.

## Consequences

- `appsettings.json` holds only non-secret defaults.
- New developers must set their User Secrets and create their local database before running the API.
