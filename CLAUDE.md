# DataTrustHub — Agent Context

## What this project is

DataTrustHub is a secure data management platform with policy-based access control.
Users belong to organizations. Policies define access rules. Clearances link users to policies.
DataItems are files classified by security level.

## Tech stack

- .NET 9, ASP.NET Core Minimal API
- Carter (endpoint auto-discovery via ICarterModule)
- Own handler interfaces, no MediatR (ADR-0012): `ICommandHandler<TCommand, TResult>` for writes and
  `IQueryHandler<TQuery, TResult>` for reads, in `Features/_Shared/Messaging`; endpoints inject the handler
  they call. Scrutor registers the handlers and wraps them in decorators (validation; later auditing)
- FluentValidation 12 (validated by the validation decorators before the handler runs)
- Entity Framework Core 9 with SQL Server
- DataTrustHub.SharedKernel: Result<T>, Error, ErrorType, Entity base classes

## Architecture: Clean Architecture with vertical slices (ADR-0012)

Four layers, dependencies pointing inward: API → Features → Domain ← Infrastructure.

- **Domain layer** (`DataTrustHub.Domain`): entities, value objects, domain errors and the ports
  (`I*Repository`, `IUnitOfWork`, `IPasswordHasher`). Business rules (invariants) live here, in entity
  methods. No EF Core.
- **Application layer** (`DataTrustHub.Features`): one use case per vertical slice, organized by feature.
- **Infrastructure layer** (`DataTrustHub.Infrastructure`): adapters for the ports: repositories,
  Mapperly mappers between persistence models (`Db*`) and entities, `DContext` as the unit of work.
- **Presentation layer** (`DataTrustHub.API`, the WebApp).

Use the terms of the ubiquitous language in `docs/domain/ubiquitous-language.md`.

```
DataTrustHub.Features/
  _Shared/               <- shared pieces (messaging, decorators, extensions) — READ ONLY
  Authentication/        <- your model: study this before writing your own slice
  DataManagement/
  Sharing/
  Administration/
  MainView/
```

Each use case folder contains:
- `Constants.cs`       — route string, tag, validation thresholds, error definitions
- `*Endpoint.cs`       — ICarterModule: request/response records + HTTP handler
- `*Handler.cs`        — ICommandHandler or IQueryHandler: orchestrates the use case, holds no business rules
- `*Validator.cs`      — AbstractValidator: input validation (shape of the request), not business rules

How a handler works:
- **Command** (changes state): load the aggregate through its repository, call the entity's behaviour,
  save once with `IUnitOfWork.SaveChangesAsync`. Never `DContext`, `Db*` or EF Core.
- **Query** (reads only): reads `DContext` with `AsNoTracking` for now. ADR-0012 step 4 moves queries to
  read models behind `I*Queries` ports; after that no handler sees `DContext`.

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

- DataTrustHub.SharedKernel — Result<T>, Error, ErrorType, ValidationError
- DataTrustHub.Domain — entities, domain errors and ports (`I*Repository`, `IUnitOfWork`, `IPasswordHasher`),
  all registered in DI
- DataTrustHub.Infrastructure.Persistance.DContext and the persistence models (`Db*`) — query handlers only,
  until ADR-0012 step 4
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
