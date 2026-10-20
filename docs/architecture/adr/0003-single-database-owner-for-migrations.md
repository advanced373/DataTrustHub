# ADR-0003: One designated Database Owner creates EF migrations

- **Status:** Accepted
- **Date:** 2026-05-30
- **Related:** [VSA design spec §2.5](../../superpowers/specs/2026-05-30-datatrusthub-vsa-multiagent-design.md), #20, #39

## Context

EF Core migrations and the model snapshot (`DContextModelSnapshot.cs`) are shared by every feature.
Two agents generating migrations in parallel produce snapshot conflicts and an ambiguous migration order.

## Decision

- An agent that changes an entity marks the line with `// NEEDS MIGRATION` and does not run `Add-Migration`.
- The orchestrator (developer) explicitly designates one agent as **Database Owner**.
  Only that agent generates migrations, and it removes the markers it covers.
- A PR with a schema change stacks on the PR that holds the latest migration, so the history stays linear.
- New non-nullable columns get an explicit default for existing rows (e.g. `IsDeleted = false`, `Role = "User"`).

## Alternatives considered

- **Every agent creates its own migration.** Snapshot conflicts on every merge.
- **One migration per sprint, written by hand.** Slower feedback; tests could not exercise the schema.

## Consequences

- Integration tests use EF InMemory, so a missing migration is not caught by tests.
  Verify schema changes against a real SQL Server (LocalDB locally). The missing `IsDeleted`
  migration broke uploads on SQL Server while all tests passed (#21).
- Schema work is serialized through one owner, which is slower but conflict-free.
