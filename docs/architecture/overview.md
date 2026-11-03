# Architecture Overview

Decisions behind this page are recorded in the [ADR index](adr/README.md). The domain model (aggregates,
invariants, ubiquitous language) is described in the [domain handbook](../domain/README.md).

## Style: Clean Architecture with vertical slices

DataTrustHub combines Clean Architecture with vertical slices (ADR-0012, which builds on ADR-0001).
Dependencies point inward: API → Features → Domain ← Infrastructure. Inside Features, code is organized
by feature and use case, not by technical layer, and feature folders do not depend on each other.

## Projects

```
DataTrustHub.SharedKernel     — Result<T>, Error, ErrorType, UserRoles (no dependencies)
DataTrustHub.Domain           — entities, value objects, domain errors and the ports
                                (I*Repository, IUnitOfWork, IPasswordHasher); no EF Core
DataTrustHub.Features         — the application layer: one vertical slice per use case
DataTrustHub.Infrastructure   — EF Core DContext (also the unit of work), persistence models (Db*),
                                Mapperly repositories, migrations, PasswordHasher
DataTrustHub.API              — ASP.NET Core Minimal API host: wires everything together
DataTrustHub.WebApp           — ASP.NET Core MVC frontend (folder DataTrustHub/), a client of the API (ADR-0005)
DataTrustHub.Features.Tests   — unit, integration (WebApplicationFactory), migration and architecture tests
```

## Request flow (API)

```
HTTP request
  -> Carter (route match via ICarterModule.AddRoutes)
  -> Endpoint: maps the request to a command or query, reads the caller id
     (ClaimsPrincipal.GetUserId) and calls the handler it injects
  -> AuditCommandDecorator (commands only, outermost: records the outcome, ADR-0016)
  -> CommandValidationDecorator / QueryValidationDecorator (FluentValidation; 400 on invalid input)
  -> ICommandHandler / IQueryHandler (the use case)
  -> Result<T>
  -> ResultExtensions.ToHttpResult: success as the endpoint maps it (200/204), 400 / 404 / 409
     for validation, not-found and conflict errors
  -> HTTP response
```

Handlers are registered by Scrutor (assembly scan) and wrapped in their decorators; the last decorator
registered is the outermost. A 401 comes from the endpoint when the caller id is missing, a 403 from the
authorization policies.

- **Commands** (change state) load an aggregate through its `I*Repository`, call the entity's behaviour
  (business rules live in the entities) and save once with `IUnitOfWork.SaveChangesAsync`. They never see
  `DContext` or `Db*` types.
- **Queries** (read only) still read `DContext` with `AsNoTracking`. Moving them behind `I*Queries` ports
  is ADR-0012 step 4 (#74).

## Slice anatomy

```
Features/<Feature>/<UseCase>/
  Constants.cs       — route, tag, validation thresholds, Error instances
  *Endpoint.cs       — ICarterModule: request/response records and the HTTP handler
  *Handler.cs        — ICommandHandler or IQueryHandler: orchestrates the use case
  *Validator.cs      — AbstractValidator: the shape of the request, not business rules
```

Feature folders today: `Authentication`, `Administration`, `DataManagement`.

## Shared pieces (`Features/_Shared`)

```
Messaging/   — ICommandHandler, IQueryHandler, the validation decorators
Auditing/    — AuditCommandDecorator, AuditScope, audit targets and constants (ADR-0016)
Extensions/  — FeaturesRegistration (Carter, Scrutor, validators, JWT, auditing),
               ResultExtensions (Result<T> to IResult), ClaimsPrincipalExtensions (caller id)
Jwt/         — JwtTokenGenerator, JwtConfig, JwtSecretGuard (the key comes from secrets, ADR-0007)
Seeding/     — AdminSeeder, AdminSeedOptions (ADR-0006, ADR-0022)
```

`_Shared` is read-only for feature work; changes need the owner's approval.

## WebApp

The WebApp (`DataTrustHub/`) is an MVC application that holds no data of its own. It signs the user in
with a cookie that carries the API's JWT as a claim, and calls the API through typed clients in
`Services/Api` (ADR-0005). Its base URL is the `Api__BaseUrl` setting.

## Adding a use case

1. Create `Features/<Feature>/<UseCase>/` with `Constants.cs`, `*Endpoint.cs`, `*Handler.cs`, `*Validator.cs`.
2. Carter discovers the endpoint and Scrutor the handler; no manual registration is needed.
3. Write the tests in `DataTrustHub.Features.Tests/<Feature>/`.
4. Follow the three-agent process (ADR-0013) and the workflow in `CLAUDE.md`.
