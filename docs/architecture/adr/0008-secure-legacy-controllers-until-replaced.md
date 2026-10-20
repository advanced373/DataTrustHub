# ADR-0008: Secure the legacy API controllers until VSA slices replace them

- **Status:** Superseded by [ADR-0010](0010-port-legacy-endpoints-and-remove-the-layered-code.md)
- **Date:** 2026-10-19
- **Related:** #25, #34, #35, #41

## Context

The legacy MVC controllers in `DataTrustHub.API/Controllers` (`DataItem`, `User`, `Organization`,
`Policy`, `Clearance`) predate the VSA authorization model and need proper authorization. Some of what they do (listing users, organizations and
policies, managing clearances) has no VSA replacement yet.

## Decision

Add proper authorization to the legacy controllers instead of deleting them:
- an authenticated user on every action;
- `DataItem`: owner-only access, and soft-deleted items hidden;
- user, organization, policy and clearance management: `Admin` role only (enabled by [ADR-0006](0006-roles-in-jwt-and-admin-seeding.md)).

Each controller is removed later, once its VSA slice exists (#5, #23).

## Alternatives considered

- **Delete the controllers now (#34, #35, closed).** Removes the risk immediately, but loses
  clearance management and the listing endpoints until #23.
- **Only add `[Authorize]`.** Any signed-in user could still read everything and delete users.

## Consequences

- Functionality is kept during the migration, at the cost of maintaining authorization code
  in code that will be deleted.
- The work is tracked in #41.
