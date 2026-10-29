# Architecture Overview

## Style: Vertical Slice Architecture

DataTrustHub uses Vertical Slice Architecture (VSA). Code is organized by feature, not by layer.
Each feature folder is a self-contained unit. There are no dependencies between feature folders.

## Project layout

```
DataTrustHub.SharedKernel     — Result<T>, Error, Entity base classes (no dependencies)
DataTrustHub.Domain           — Legacy domain entities (being phased out into slices)
DataTrustHub.Infrastructure   — EF Core DContext, DB models, PasswordHasher
DataTrustHub.Application      — Legacy CQRS handlers (being phased out into slices)
DataTrustHub.Features         — All new feature slices (VSA)
DataTrustHub.API              — ASP.NET Core host: wires everything together
DataTrustHub.Features.Tests   — Integration tests using WebApplicationFactory
DataTrustHub.WebApp           — Razor/MVC frontend (separate epic, not in scope here)
```

## Request flow

```
HTTP Request
  -> Carter (route match via ICarterModule.AddRoutes)
  -> Endpoint Handler (maps request to MediatR command)
  -> ValidationPipelineBehavior (runs FluentValidation before handler)
  -> IRequestHandler (business logic: DB read/write, token generation, etc.)
  -> Result<T> returned
  -> Endpoint maps Result<T> to IResult (200/400/401/404/409)
  -> HTTP Response
```

## Slice anatomy

Every use case follows this exact pattern:

```
Features/<Feature>/<UseCase>/
  Constants.cs       — route, tag, validation thresholds, Error instances
  *Endpoint.cs       — ICarterModule with request/response records and HTTP handler
  *Handler.cs        — IRequestHandler with business logic
  *Validator.cs      — AbstractValidator for the command/query
```

## Shared infrastructure (_Shared)

```
Features/_Shared/
  Behaviors/ValidationPipelineBehavior.cs  — runs validators before every handler
  Extensions/FeaturesRegistration.cs       — registers Carter, MediatR, validators, JWT
  Extensions/ResultExtensions.cs           — maps Result<T> to IResult
```

## Adding a new feature

1. Create `Features/<YourFeature>/<UseCase>/` folder
2. Create `Constants.cs`, `*Endpoint.cs`, `*Handler.cs`, `*Validator.cs`
3. Carter and MediatR auto-discover your endpoint and handler — no manual registration needed
4. Write integration tests in `Features.Tests/<YourFeature>/`
