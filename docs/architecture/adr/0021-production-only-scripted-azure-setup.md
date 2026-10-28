# ADR-0021: Production only, in Poland Central, set up by script instead of Bicep and Key Vault

- **Status:** Accepted
- **Date:** 2026-10-28
- **Related:** #101, #98, #102; amends [ADR-0017](0017-azure-deployment.md) and
  [ADR-0018](0018-staging-and-production-with-migrations-in-ci-cd.md)

## Context

ADR-0017 and ADR-0018 planned two fully separate environments (staging and production), described
in Bicep, with the application secrets in one Key Vault per environment.

By 2026-10-28 the owner had already created the production resources by hand in the portal, aiming
for a cost close to zero, in one resource group `DataTrustHub`:
- an App Service plan on the **F1** (free) tier, Linux, in **Poland Central**, with two web apps,
  the API and the WebApp, both on `DOTNETCORE|10.0` (ADR-0020);
- an Azure SQL server with Microsoft Entra-only authentication and one serverless database on the
  **Azure SQL free offer**, in Germany West Central;
- Application Insights, connected to both apps.

The owner develops and runs the platform alone. It is not delivered to other customers, so its
infrastructure is created once, not repeatedly. Reviewing the plan with these facts:
- Bicep's main benefits, identical environments and repeatable creation, are not needed with one
  environment created once. Its cost remains: every resource is a full PUT, so any property the
  template omits is reset, and the templates must be kept in line with the portal.
- Applying Bicep from CD would give the deploy identity Contributor on the resource group, far more
  than the database and code-deploy rights in ADR-0018 §4.
- Without a tool that rewrites the app settings on every release, Key Vault's main benefit here is
  gone. The owner judged it unnecessary for the current scenario.
- The apps and the database in different regions add a cross-region round trip to every query.

The owner decided on 2026-10-28:
- production only for now;
- everything in Poland Central, keeping the free plan;
- no Bicep and no Key Vault.

## Decision

1. **One environment, `production`.** Staging (ADR-0018 §1) is postponed. Bringing it back needs
   a new ADR, and is due at the first of:
   - the first real users outside the owner;
   - the first migration that must preserve production data;
   - the first breaking schema change (expand/contract across releases, ADR-0018 §5).

   Until then, the safety net for migrations is CI (ADR-0018 §3): every migration is applied from
   an empty database to SQL Server 2022 on every PR. The release still waits for the owner's
   approval on the GitHub environment `production`.
2. **Everything in Poland Central.** The existing plan and web apps stay. The SQL server and its
   database are recreated in Poland Central, on the free offer and with Entra-only authentication,
   and the server in Germany West Central is deleted. Its database holds no data yet: the API has
   never run against it.
3. **No Bicep: a scripted, one-time setup** replaces ADR-0017 §3. `infra/` holds:
   - an idempotent Azure CLI script, run by the owner, that sets what the portal did not:
     - a system-assigned managed identity on the API only (the WebApp holds no secret);
     - the API's app settings: the SQL connection string with
       `Authentication=Active Directory Managed Identity` (no password), `Jwt__Secret`,
       `AdminSeed__Email` and `AdminSeed__Password`, keeping the Application Insights settings;
     - the WebApp's `Api__BaseUrl`, from the API's host name;
     - FTP and basic publishing credentials disabled;
   - an idempotent T-SQL script, run once by the owner as the server's Entra admin, that creates the
     database users and roles of ADR-0018 §4 for the API's identity and the deploy identity;
   - a `README.md` listing the resources, their tiers and settings, and how to run the scripts.

   The script never contains a secret value. It generates the JWT signing key, or reads values from
   the owner at run time, and writes them straight to the app settings.
4. **No Key Vault** (changes ADR-0017 §2). The application secrets live in the API's App Service
   app settings, encrypted at rest by the platform, and never in the repository or in CI. The API
   reaches SQL with its managed identity, so no database password exists anywhere.
5. **CD does not touch infrastructure.** The release workflow (#102) only applies migrations and
   deploys code, with the deploy identity and rights of ADR-0018 §4. There is no infrastructure
   identity.
6. **Free tiers are accepted for the MVP**, with their limits:
   - F1: 60 CPU minutes a day for the plan, no Always On, so cold starts;
   - the SQL free offer: a monthly vCore-seconds quota, auto-pause when idle and when the quota is
     exhausted (production then stops until the next month).

   Because of them, the code must have a health endpoint that does not touch the database, and SQL
   connections must retry on transient errors. CD wakes the database with retries before migrating,
   and retries its smoke check. Moving to paid tiers (for example B1, and SQL billing over the free
   limit) is due with the first real users.

## Alternatives considered

- **Bicep applied from CD** (the owner's first choice). Rejected after review: the deploy pipeline
  would need Contributor on the resource group, which can also make itself Entra admin of the SQL
  server, for infrastructure that is created once.
- **Bicep applied by hand.** Keeps CD narrow, but still costs exact templates of resources that
  already exist, for no repeatability benefit with one environment.
- **Keep Key Vault** (ADR-0017 §2). Costs almost nothing, and gives audit and rotation. Rejected
  for now as unnecessary: nothing rewrites the settings, there is one secret holder, and one owner.
- **A free staging environment** (second F1 plan, second free database). Doubles the manual setup
  (identities, OIDC trust, SQL users, secrets) with no data yet to protect.
- **Move the apps to the database's region** (Germany West Central). Rejected: the owner prefers
  Poland Central, where the free plan already runs; the database is empty, so it moves instead.

## Consequences

**Easier:**
- No templates to maintain, and no risk of a deploy resetting settings it did not declare.
- The deploy identity keeps the narrow rights of ADR-0018 §4.
- Cost close to zero.
- Apps and database in one region.

**Harder:**
- The infrastructure is not reproducible from code. `infra/README.md` and the scripts are the
  record; changes made in the portal must be written back there by hand.
- Anyone with read access to the API's configuration can read the secrets. That includes the
  deploy identity, which needs Website Contributor on the API to deploy code. It is protected by
  the `production` environment (only `main`, owner approval) and federated credentials.
  Moving to Key Vault is due with a second environment or a second person with Azure access.
- Production is the first place a release runs on Azure, without a staging rehearsal.
- Free-tier limits can stop production (CPU quota, SQL quota), with no SLA.

**Changes to ADR-0017:**
- §2: secrets in App Service app settings, not Key Vault; the API still reaches SQL with a managed
  identity.
- §3: a one-time scripted setup in `infra/`, not Bicep.
- §6: production only, in Poland Central.

**Changes to ADR-0018:**
- §1–2: production only for now, with the conditions above for bringing staging back. The release
  flow keeps build, migrate, deploy and smoke check, with the owner's approval before production.
- §4 is unchanged.

**Follow-up:**
- #101: rescoped from Bicep to the scripted setup and `infra/README.md`.
- #98: rescoped from Key Vault to the app settings set by the script.
- #102: the release workflow, production only, with the database wake-up and retries.
- Code before the first deploy: a health endpoint without database access, and SQL connection
  retries.
- The owner: recreate the SQL server in Poland Central on the free offer, and delete the one in
  Germany West Central.
