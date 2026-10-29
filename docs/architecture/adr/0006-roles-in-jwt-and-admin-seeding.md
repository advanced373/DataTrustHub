# ADR-0006: User roles in the JWT, first admin seeded from configuration

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** #27, #39, #40; amended by [ADR-0014](0014-organizations-as-tenants-with-memberships.md)

## Context

Admin endpoints required `RequireRole("Admin")`, but users had no role and the JWT had no role
claim, so no real user could ever reach them. A first admin also has to exist somehow.

## Decision

- `DbUser.Role` is a string, with values from `SharedKernel.UserRoles` (`User`, `Admin`). It defaults to `User`.
- `JwtTokenGenerator` emits the role as the short `role` claim. JwtBearer maps it to
  `ClaimTypes.Role`, so `RequireRole` and `[Authorize(Roles = ...)]` work with real tokens.
- The first admin is seeded at startup by `AdminSeeder` from `AdminSeed:Email` / `AdminSeed:Password`
  (User Secrets locally). It creates the user, or promotes an existing user with that email; an existing
  password is never changed. When the section is not configured, nothing happens.

## Alternatives considered

- **`IsAdmin` bool.** Simpler, but every new role would need another column and migration.
- **Promote the admin with a manual SQL script.** Not repeatable, easy to forget, hard to test.
- **Hard-coded seed credentials.** Credentials would end up in a public repo.

## Consequences

- A role change takes effect at the next sign-in, when the user gets a new token.
- Tests must not inherit a developer's User Secrets. `ApiFactory` overrides `AdminSeed` with empty values.
- Finer-grained permissions (policies, clearances) remain separate from roles and come with #3 and #23.
