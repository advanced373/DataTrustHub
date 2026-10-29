# ADR-0012: Clean Architecture with vertical slices, repositories for commands, and no MediatR

- **Status:** Accepted (2026-10-21; step 1 done in #75 / #77)
- **Date:** 2026-10-21
- **Refines:** [ADR-0001](0001-vertical-slice-architecture.md) (slices stay; the layers around them are defined here)
- **Related:** [ADR-0010](0010-port-legacy-endpoints-and-remove-the-layered-code.md), [ADR-0011](0011-data-access-by-policy-and-classification-level.md), #3, #24

## Context

After ADR-0010, the slices in `DataTrustHub.Features` work directly with `DContext` and with the
EF models `Db*` in `DataTrustHub.Infrastructure`. `DataTrustHub.Domain` contains only `IPasswordHasher`.
This creates three problems:

- **Business rules have no home of their own.** The read rule of ADR-0011 is a LINQ expression in
  `Features/DataManagement`. Owner-only delete, the `409` checks and the account-delete cascade
  live in handlers. Sharing (#3) needs the same read rule, and Rule 2 (no cross-feature imports)
  forbids taking it from DataManagement.
- **The features work with persistence models, not domain entities.** The `Db*` classes are table
  shapes with public setters and no invariants.
- **MediatR is no longer free software.** Since version 13 (July 2025), MediatR is dual-licensed
  (RPL-1.5 or commercial). The free Community edition covers organisations under USD 5M gross
  annual revenue, but still needs a registered license key, and its terms can change. 12.5.0 is the
  last Apache-2.0 release. DataTrustHub uses MediatR 13.0.0 in `Features` and `SharedKernel`.

Possible future microservices were also considered.

## Decision

### 1. Clean Architecture, with vertical slices as the application layer

```
DataTrustHub.API             host: Program.cs, configuration, auth wiring
DataTrustHub.Features        application layer, organised as vertical slices (ADR-0001)
DataTrustHub.Domain          entities with behaviour, repository and unit-of-work interfaces,
                             read-model interfaces; no EF Core, no Infrastructure
DataTrustHub.Infrastructure  EF Core: Db* models, DContext, migrations, repositories,
                             unit of work, read-side queries, PasswordHasher
DataTrustHub.SharedKernel    Result, Error, UserRoles; no third-party packages
```

Dependencies point inward: API → Features → Domain ← Infrastructure.
`Features` keeps its project reference to `Infrastructure` only for DI wiring. It must not use
`DContext` or `Db*` types, and an architecture test enforces this.

### 2. Commands go through domain entities and repositories; queries use a read side

- **Commands** (create, delete, classify, grant):
  1. load domain entities through `I*Repository` interfaces in `Domain`;
  2. apply the rule on the entity (for example `dataItem.MarkDeleted(requesterId)`);
  3. persist with `IUnitOfWork.SaveChangesAsync`, once per command. A multi-aggregate change, such
     as deleting an account with its clearances and data, stays one transaction.
- **Repositories** live in `Infrastructure`, work with the `Db*` EF models, and convert them to and
  from domain entities with **Riok.Mapperly** (Apache-2.0, source-generated at compile time; an
  unmapped property is a build warning that we treat as an error). AutoMapper is not used: it is
  commercial since its v15.
- **Queries** (lists and details with joins) do not load entities. They call read-side interfaces
  (`I*Queries`, declared in `Domain/ReadModels`, implemented in `Infrastructure`) that project
  straight to read models in SQL.
- **The read rule of ADR-0011 has two forms:**
  - `DataItem.IsReadableBy(clearances)` in `Domain`, for commands;
  - the SQL filter in the read side, for queries.

  A test runs both on the same data and must give the same answers.

### 3. Replace MediatR with our own handler interfaces

Each slice declares `ICommandHandler<TCommand, TResult>` or `IQueryHandler<TQuery, TResult>`. These
are our own interfaces, in `Features/_Shared/Messaging`. Endpoints inject the handler they need
directly, so no mediator or service locator is involved.

Cross-cutting behaviour is added as **decorators** registered with **Scrutor** (MIT):
- validation, which replaces `ValidationPipelineBehavior`;
- later, auditing (#24).

`SharedKernel.IDomainEvent` drops its `INotification` base. Domain events, if we need them, get our
own dispatcher. MediatR is removed from every project.

### 4. Migration plan (each step one small PR; the tests stay green after each)

1. **Messaging:** our own handler interfaces, a validation decorator with Scrutor, and MediatR
   removed. This is a mechanical change across the slices, independent of the steps below.
2. **Foundation:**
   - domain entities with private setters and factories;
   - `I*Repository` and `IUnitOfWork`;
   - Mapperly mappers and repositories in `Infrastructure`.

   The schema does not change; a migration generated afterwards must be empty.
3. **Commands per feature area,** one PR each: DataManagement, Administration, Authentication.
4. **Read side:** `I*Queries` for the list and detail slices, plus the test that compares the two
   forms of the read rule.
5. **Enforcement:**
   - architecture tests: `Features` does not use `DContext`/`Db*`; `Domain` does not reference
     EF Core; entities have private setters;
   - `CLAUDE.md` updated: "commands use repositories, queries use `I*Queries`" replaces "inject
     DContext directly".

## Alternatives considered

- **Keep `DContext` and `Db*` in the slices, delete Domain.** Simplest, but rules stay scattered
  and the read rule cannot be shared with Sharing (#3).
- **Domain entities mapped directly by EF (no `Db*`, no mappers).** Less code. Rejected because we
  want the domain free of persistence concerns and able to differ from the table shapes.
- **Modular monolith (separate modules and schemas per context: Identity, AccessControl,
  DataCatalog).** It prepares for microservices. Rejected for now:
  - the access rule needs strong consistency between data and clearances, and with split modules
    a revoked clearance could still be honoured through an event-fed copy;
  - the system is small (three contexts, about 30 endpoints), and nothing yet requires separate
    teams, deployment or scaling.
- **Stay on MediatR 13+ under the Community license.** It probably qualifies today, but it needs a
  license key and ties the project to terms that may change if the product is ever used
  commercially.
- **Pin MediatR 12.5 (Apache-2.0).** Free, but frozen: no further fixes or support for new .NET versions.
- **martinothamar/Mediator (MIT, source-generated, MediatR-like API).** A smaller change than our own
  interfaces, and a valid fallback. Our own interfaces were preferred because a vertical slice
  calls exactly one handler, so a mediator adds indirection without benefit.

## Consequences

- Rules live on entities and are unit-testable without HTTP or a database.
- The read rule is reusable by Sharing (#3).
- More code per command: repository methods, mappers and the unit of work. The read rule exists
  in two forms, kept in line by a test.
- No paid or key-gated dependencies remain (MediatR and AutoMapper are excluded). The remaining
  packages are Carter (MIT), FluentValidation (Apache-2.0), Mapperly (Apache-2.0) and Scrutor (MIT).
- **When to revisit the modular monolith:** separate teams owning parts of the product, independent
  deployment or scaling needs, or one context growing much faster than the others. Until then,
  Rule 2, the architecture tests and repositories at context boundaries keep a later split
  possible: each cross-context access is an explicit interface.
- `CLAUDE.md` and ADR-0001's guidance on importing `DContext` and `Db*` models change in step 5.
