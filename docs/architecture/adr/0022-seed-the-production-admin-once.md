# ADR-0022: Seed the production admin once, then remove the seed settings

- **Status:** Accepted
- **Date:** 2026-10-29
- **Related:** #147; amends [ADR-0006](0006-roles-in-jwt-and-admin-seeding.md) and [ADR-0021](0021-production-only-scripted-azure-setup.md)

## Context

ADR-0006 seeds the first admin at startup from `AdminSeed:Email` / `AdminSeed:Password`, and
ADR-0021 has `infra/setup.ps1` put both values in the API's app settings. The seeder runs on every
start of the API, and every run opens a database connection to check that the admin exists.

In production the API runs on the F1 plan without Always On, and the database is the serverless free
offer that pauses itself when idle (ADR-0021). The API cold-starts after every deploy, platform restart
and idle period, so each start wakes the database. A wake-up keeps it running for at least the
auto-pause delay, about 1,800 vCore-seconds at the minimum size. That is about 2 % of the monthly free
quota of 100,000, spent only to confirm that an admin created long ago still exists.

## Decision

- In production, the admin is seeded **once**. After the first successful seed, the owner deletes
  `AdminSeed__Email` and `AdminSeed__Password` from the API's app settings. The seeder already does
  nothing when the section is not configured (ADR-0006), so no application code changes.
- `infra/setup.ps1` no longer asks for the seed settings when they are missing. It writes them only
  when the owner asks for a seed explicitly, with a new switch, for the first admin or to recover access.
- `infra/README.md` describes the procedure: seed, check the admin can sign in, delete the two settings.
- Locally (User Secrets) and in tests nothing changes: the seeder still runs at every start when configured.

## Alternatives considered

- **Seed from the release workflow** instead of at startup. The workflow would need the admin's
  e-mail and password, as a GitHub secret or read from the app settings, which ADR-0018 and ADR-0021
  keep out of CI/CD. More moving parts for a once-only task.
- **A marker setting** (for example `AdminSeed__Done`) that makes the seeder skip without a query. It
  needs the same manual step after the first seed as removing the settings, plus code to read it.
- **Keep seeding at every start.** Simple, but it spends the free database quota on every cold start.

## Consequences

- Cold starts of the API no longer touch the database; the database wakes only for real requests and
  for the release's migrations.
- The admin's password no longer stays in the app settings after the first seed, so fewer people and
  tools can read it.
- One manual step after the first seed. Forgetting it costs quota, not correctness.
- To recover a lost admin account, the owner seeds again with the switch, then deletes the settings again.
  The seeder never changes an existing user's password (ADR-0006), so recovery of a forgotten password
  still needs another way (a password reset feature, or a new admin e-mail).
