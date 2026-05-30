# DataTrustHub — Agent Context

## What this project is

DataTrustHub is a secure data management platform with policy-based access control.
Users belong to organizations. Policies define access rules. Clearances link users to policies.
DataItems are files classified by security level.

## Tech stack

- .NET 9, ASP.NET Core Minimal API
- Carter (endpoint auto-discovery via ICarterModule)
- MediatR 13 (CQRS within each slice)
- FluentValidation 12 (validated via ValidationPipelineBehavior)
- Entity Framework Core 9 with SQL Server
- DataTrustHub.SharedKernel: Result<T>, Error, ErrorType, Entity base classes

## Architecture: Vertical Slice Architecture

Code is organized by feature, not by layer. Your feature folder contains everything it needs.

```
DataTrustHub.Features/
  _Shared/               <- shared infrastructure (behaviors, extensions) — READ ONLY
  Authentication/        <- your model: study this before writing your own slice
  DataManagement/
  Sharing/
  Administration/
  MainView/
```

Each use case folder contains:
- `Constants.cs`       — route string, tag, validation thresholds, error definitions
- `*Endpoint.cs`       — ICarterModule: request/response records + HTTP handler
- `*Handler.cs`        — IRequestHandler: business logic only
- `*Validator.cs`      — AbstractValidator: input rules

## Rule 1 — Strict isolation

You work EXCLUSIVELY in `Features/<YourFeatureName>/`.
Do NOT modify files in other feature folders.
Do NOT modify `Program.cs`, `appsettings.json`, or infrastructure without explicit approval.

## Rule 2 — No cross-feature imports

Never import from another feature namespace.
Example: using DataTrustHub.Features.Authentication inside DataManagement is FORBIDDEN.
Use DataTrustHub.SharedKernel types (Result, Error) to communicate outcomes.

## Rule 3 — DB migrations

If your entities need schema changes, mark the line with // NEEDS MIGRATION.
Do NOT run Add-Migration without the orchestrator designating you as "Database Owner".

## What you can import

- DataTrustHub.SharedKernel — Result<T>, Error, ErrorType, Entity, ValidationError
- DataTrustHub.Infrastructure.Persistance.DContext — inject via DI, use directly
- DataTrustHub.Infrastructure.Persistance.Model — DB entity models (DbUser, DbOrganization, etc.)
- DataTrustHub.Domain.User.IPasswordHasher — already registered in DI
- NuGet packages already in DataTrustHub.Features.csproj

## Code style rules (enforced in every PR)

- camelCase for local variables and parameters
- PascalCase for types, methods, properties, and constants
- All comments in English
- No magic strings or numbers — extract to Constants.cs in your slice
- Functions max 30 lines — extract private helper methods if needed
- Depend on interfaces — never new SomeService() inside a handler
- One responsibility per class — handlers handle, validators validate, endpoints route

## Running tests

```
dotnet test DataTrustHub.Features.Tests/
```

## Building

```
dotnet build DataTrustHub.sln
```

## Your task

Read the GitHub issue assigned to your worktree. Implement the feature slice in
Features/<YourFeatureName>/. Use Authentication/ as the reference model.
Open a PR when all tests pass.
