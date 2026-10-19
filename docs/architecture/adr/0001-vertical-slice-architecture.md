# ADR-0001: Vertical Slice Architecture for new features

- **Status:** Accepted
- **Date:** 2026-05-30
- **Related:** [VSA design spec](../../superpowers/specs/2026-05-30-datatrusthub-vsa-multiagent-design.md), #17

## Context

The project started with Clean Architecture (`SharedKernel`, `Domain`, `Application`, `Infrastructure`, `API`).
Every feature touched several cross-cutting layers, so Claude Code agents working on separate
GitHub issues in parallel kept editing the same projects and conflicting.

## Decision

New features are written as vertical slices in `DataTrustHub.Features`, organized by feature
(`Authentication`, `DataManagement`, `Sharing`, `Administration`, `MainView`) and then by use case.
Each use case folder holds `Constants.cs`, `*Endpoint.cs` (Carter `ICarterModule`),
`*Handler.cs` (MediatR) and `*Validator.cs` (FluentValidation, run by `ValidationPipelineBehavior`).
Handlers return `Result<T>` from `SharedKernel`.

Rules:
- Feature folders do not import from each other; they share only `SharedKernel`, `Infrastructure` and `_Shared`.
- No new code goes into the legacy `Application` / `Domain` projects. They are phased out feature by feature.

## Alternatives considered

- **Keep Clean Architecture.** Parallel agents would keep colliding in shared layers.
- **Big-bang rewrite.** Too risky; the migration happens one slice at a time instead.

## Consequences

- One agent = one issue = one worktree = one feature folder, so parallel work rarely conflicts.
- Some duplication between slices is accepted in exchange for isolation.
- Legacy code coexists during the migration, so its endpoints must be secured or retired (see #25, #41).
- EF migrations remain a shared hotspot (see [ADR-0003](0003-single-database-owner-for-migrations.md)).
