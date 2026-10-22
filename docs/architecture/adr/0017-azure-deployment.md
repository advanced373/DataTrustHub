# ADR-0017: Deploy the MVP to Azure with GitHub Actions

- **Status:** Accepted
- **Date:** 2026-10-22
- **Related:** #22, #26; amended by [ADR-0018](0018-staging-and-production-with-migrations-in-ci-cd.md); [ADR-0007](0007-local-secrets-in-user-secrets.md),
  [ADR-0009](0009-gitflow-and-small-stacked-prs.md)

## Context

The user decided on 2026-10-22 that delivering the MVP means a deployment in the cloud, on Azure.
Today the application runs only locally, with SQL Server LocalDB and user-secrets (ADR-0007). It
has no CI. The repository is public, so no secret may live in the repository or in the workflow
files.

## Decision

1. **Hosting:**
   - two **Azure App Service** web apps on Linux, .NET 9: the API and the WebApp;
   - the WebApp calls the API over HTTPS (ADR-0005);
   - one **Azure SQL Database**.
2. **Secrets:** they live in **Azure Key Vault**. The SQL connection, the JWT signing key and the
   admin seed are all there.
   - The apps read Key Vault with a **managed identity**. They reach SQL with Microsoft Entra
     authentication where possible, so no password is needed.
   - `appsettings.json` holds no secrets.
3. **Infrastructure as code:** the resources are described in **Bicep** under `infra/`, in one
   resource group per environment.
4. **CI:** GitHub Actions builds and tests every PR to `develop` and `main`. The checks are then
   made required by branch protection.
5. **CD:**
   - every merge to `main` (a release, ADR-0009) deploys to Azure;
   - GitHub signs in to Azure with **OpenID Connect** (federated credentials), with no stored
     cloud secrets;
   - EF Core migrations run as a separate step in the deploy job. The apps never migrate on
     startup.
6. **Environments:** one environment, `production`, for the MVP. A `staging` environment can be
   added later as an App Service deployment slot.
   *Amended by ADR-0018: two environments, `staging` and `production`, each with its own
   database; a release reaches production only after approval on staging.*
7. **Observability:** **Application Insights** for both apps, for technical logs and failures. The
   business audit trail stays in the database (ADR-0016).

## Alternatives considered

- **AWS or a VPS with Docker.** Rejected: Azure fits .NET and SQL Server natively, and the user
  chose it.
- **Containers (Azure Container Apps).** Postponed: App Service is simpler for two .NET apps. The
  apps can move to containers later without code changes.
- **Secrets in GitHub Actions secrets or App Service settings.** Rejected for the application
  secrets. Key Vault with managed identity keeps them out of CI and gives rotation and access
  control.
- **Migrate on application startup.** Rejected: it races when several instances start, and it
  gives the app schema rights at runtime.

## Consequences

**Easier:**
- Releases are repeatable.
- Every PR is checked by CI.
- Secrets never touch the repository.

**Harder:**
- The project needs an Azure subscription and its costs. The user owns these, and only the user
  can create the subscription and the OIDC trust.
- Configuration must work in two modes: user-secrets locally, Key Vault in Azure.
- File content is still stored in SQL, as base64. In Azure SQL that is costly and slow for large
  files. Blob storage (#26) is recommended before or soon after the release.

**Follow-up (new issues):**
- CI workflow;
- Bicep infrastructure;
- production configuration through Key Vault;
- deploy workflow with migrations;
- the first release (#22).
