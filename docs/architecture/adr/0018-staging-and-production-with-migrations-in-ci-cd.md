# ADR-0018: Staging and production environments, and database migrations in CI/CD

- **Status:** Accepted
- **Date:** 2026-10-22; amended 2026-10-28 (no firewall rights for the deploy identity)
- **Related:** #101, #102, #108, #98; amends [ADR-0017](0017-azure-deployment.md); amended by [ADR-0020](0020-target-dotnet-10-lts.md) (.NET 10) and [ADR-0021](0021-production-only-scripted-azure-setup.md) (production only);
  [ADR-0003](0003-single-database-owner-for-migrations.md),
  [ADR-0009](0009-gitflow-and-small-stacked-prs.md)

## Context

ADR-0017 decided:
- that EF Core migrations run as a separate step of the deploy job and never on startup;
- one `production` environment for the MVP, with staging left for later.

It did not say how the migrations are built, checked and applied, or who holds rights on the
schema. Today nothing checks the migrations: the tests run on EF InMemory and SQLite, so a broken
migration would first fail in Azure.

The user decided on 2026-10-22:
- a release goes first to a **staging** environment and reaches **production** only after
  approval;
- staging and production each have **their own database**.

## Decision

### 1. Two environments, fully separate

`staging` and `production` each have their own resource group, described by the same Bicep
templates with one parameter file per environment. Each one holds its own:
- App Service plan, with the two Linux web apps (API, WebApp);
- Azure SQL database;
- Key Vault, with its own JWT signing key, admin seed and connection settings;
- managed identities for the apps;
- Application Insights.

No identity, secret or database is shared between the two environments. Staging may use smaller
tiers.

**Staging holds synthetic data only.** Production data is never copied to staging, because it
contains classified data (ADR-0011).

### 2. Release flow

A merge to `main` (a release, ADR-0009) runs one workflow. Each job starts only when the previous
one succeeded:

1. **Build:**
   - `dotnet publish` for the API and the WebApp;
   - `dotnet ef migrations bundle` for linux-x64, which produces `efbundle`. The bundle is
     framework-dependent: a self-contained single-file bundle cannot discover the API's Carter
     modules, and the runners have .NET 9;
   - `dotnet ef migrations script --idempotent`, kept for review.

   All three are built **once**, from the release commit, and the same artifacts are promoted to
   both environments.
2. **Staging:** migrate the staging database with the bundle, deploy both apps, then run a smoke
   check. This needs no approval.
3. **Approval:** the GitHub environment `production` requires the repository owner's approval.
   Before approving, the owner can check two things:
   - the SQL script;
   - that staging already ran the same migrations and passed the smoke check.
4. **Production:** migrate the production database with the same bundle, deploy the same app
   packages, then run a smoke check.

### 3. CI checks on every PR (#108)

- `dotnet ef migrations has-pending-model-changes` fails the build when the model has changes that
  no migration covers.
- All migrations are applied, from an empty database, to SQL Server 2022 in a
  container started by the job with `docker run`, not a `services:` block, so that the SA password
  is generated at run time and never written in the repository. SQL Server containers need a Linux
  runner, which the CI already uses.
- The idempotent SQL script is uploaded as an artifact.
- `dotnet-ef` is pinned to 9.0.x in a local tool manifest, `.config/dotnet-tools.json`.

### 4. Who holds rights on the schema

| Identity | Database rights |
|---|---|
| deploy identity of the environment: a GitHub OIDC federated credential, scoped to that GitHub environment | `db_ddladmin`, `db_datareader`, `db_datawriter`: applies migrations |
| managed identity of the API | `db_datareader`, `db_datawriter` only, no DDL |

- Each environment has its own deploy identity, so the staging credential cannot reach production.
- The bundle connects with Microsoft Entra authentication from the OIDC sign-in, with no password.
- GitHub-hosted runners have no fixed IP address. The migrate job therefore:
  - opens a firewall rule for the runner's address;
  - removes it at the end, even when the step fails.

### 5. Rules for migrations

- **Backward compatible (expand, then contract).** During a deploy, the previous version of the
  application runs against the new schema. A breaking change is split across releases. For
  example, a column rename:
  - first release: add the new column and copy the data;
  - next release: drop the old column.
- **No automatic down migrations.** If a migration breaks something, the fix goes forward in a new
  release. As a last resort, Azure SQL point-in-time restore brings the database back.
- **The first deploy starts from empty databases,** so every migration runs from the start.
- **ADR-0003 is unchanged.** Only the designated Database Owner writes migrations. The pipeline
  only applies them.

## Amendments (2026-10-28, #102)

1. **No firewall rule per run; the deploy identity has no right on the SQL server.** §4 had the
   migrate job open a firewall rule for the runner's address and remove it at the end, because
   GitHub-hosted runners have no fixed address. Production now has the rule
   `AllowAllWindowsAzureIps` (ADR-0021), which the web apps need anyway, and GitHub-hosted runners
   run in Azure, so that rule already lets them in. The owner decided on 2026-10-28 to rely on it:
   - the deploy identity gets only `Website Contributor` on the two web apps, plus its database
     roles (`db_ddladmin`, `db_datareader`, `db_datawriter`);
   - the migrate job opens and removes no firewall rule.

   This is not guaranteed by GitHub. If the migrate step ever fails with a firewall error, the
   rule-per-run of §4 comes back, with a custom role limited to
   `Microsoft.Sql/servers/firewallRules/*` on the server. Security is unchanged: the server
   accepts Microsoft Entra authentication only, so who can reach the port matters less than who
   has a user in the database.
2. **The deploy identity is an app registration**, `DataTrustHub-Deploy`, with one federated
   credential for the GitHub environment `production`. Its setup is recorded in
   `infra/README.md`.

## Alternatives considered

- **Staging as an App Service deployment slot** (ADR-0017 §6). Rejected:
  - a slot shares the plan, and so the load, with production;
  - slots need the Standard tier;
  - a separate database per slot depends on sticky settings, which is easy to get wrong;
  - with swaps, the order of migrating and deploying is no longer obvious.
- **Applying the idempotent SQL script** with `sqlcmd` or `azure/sql-action`. Rejected as the
  apply step: the bundle is EF Core's own tool and applies only the missing migrations. The script
  is still produced, for review.
- **Building separately for each environment.** Rejected: production must run exactly what
  staging checked.
- **One deploy identity for both environments.** Rejected: a leaked staging credential would reach
  production.
- **Copying production data to staging** for realistic tests. Rejected: it would put classified
  data in a less protected environment.

## Consequences

**Easier:**
- Every migration runs three times before it reaches production:
  - from scratch in CI;
  - on the staging database;
  - only then on production.
- Production runs exactly the artifacts that passed on staging.
- The applications cannot change the schema.

**Harder:**
- Two sets of Azure resources, and so two costs. Staging on small tiers keeps it low.
- Breaking schema changes take two releases.
- The repository owner must create the two GitHub environments, the required reviewer on
  `production`, and one OIDC trust per environment.

**Changes to ADR-0017:**
- §6: two environments, `staging` and `production`, instead of one, each with its own database;
  staging is not a deployment slot.
- §5: a release deploys to staging first, and to production after approval.

**Follow-up:**
- #108: the migration checks in CI;
- #101: Bicep with one parameter file per environment;
- #98: one Key Vault per environment;
- #102: the release workflow described in §2 and §4.
