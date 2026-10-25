# Architecture Decision Records

An ADR records one architectural decision: the context, what was decided and its consequences.
ADRs are never rewritten. When a decision changes, write a new ADR that supersedes the old one,
and set the old one's status to `Superseded by ADR-NNNN`.

## Adding an ADR

1. Copy [`template.md`](template.md) to `NNNN-short-title.md`, using the next free number.
2. Fill in the context, decision and consequences. Link the related issues and PRs.
3. Add a row to the index below in the same PR.

Status values: `Proposed`, `Accepted`, `Superseded by ADR-NNNN`, `Deprecated`.

## Index

| ADR | Title | Status | Date |
|---|---|---|---|
| [0001](0001-vertical-slice-architecture.md) | Vertical Slice Architecture for new features | Accepted | 2026-05-30 |
| [0002](0002-relational-database-sql-server.md) | Keep a relational database (SQL Server) | Accepted | 2026-10-19 |
| [0003](0003-single-database-owner-for-migrations.md) | One designated Database Owner creates EF migrations | Accepted | 2026-05-30 |
| [0004](0004-owner-only-soft-delete.md) | Owner-only soft delete for data items | Accepted | 2026-06-16 |
| [0005](0005-mvc-webapp-as-api-client.md) | MVC WebApp as a client of the API, JWT in the auth cookie | Accepted | 2026-10-19 |
| [0006](0006-roles-in-jwt-and-admin-seeding.md) | User roles in the JWT, first admin seeded from configuration | Accepted | 2026-10-19 |
| [0007](0007-local-secrets-in-user-secrets.md) | Local configuration and secrets in User Secrets | Accepted | 2026-10-19 |
| [0008](0008-secure-legacy-controllers-until-replaced.md) | Secure the legacy API controllers until VSA slices replace them | Superseded by [0010](0010-port-legacy-endpoints-and-remove-the-layered-code.md) | 2026-10-19 |
| [0009](0009-gitflow-and-small-stacked-prs.md) | GitFlow with small, stacked PRs and explicit issue tracking | Accepted | 2026-10-19 |
| [0010](0010-port-legacy-endpoints-and-remove-the-layered-code.md) | Port the legacy endpoints to VSA slices and remove the layered code | Accepted | 2026-10-20 |
| [0011](0011-data-access-by-policy-and-classification-level.md) | Data access by policy and classification level, need-to-know | Accepted | 2026-10-21 |
| [0012](0012-clean-architecture-with-vertical-slices.md) | Clean Architecture with vertical slices, repositories for commands, and no MediatR | Accepted | 2026-10-21 |
| [0013](0013-three-agent-development-process.md) | Three-agent development process: implementer, tester, reviewer | Accepted | 2026-10-21 |
| [0014](0014-organizations-as-tenants-with-memberships.md) | Organizations as tenants, with memberships, organization admins and guests | Accepted | 2026-10-22 |
| [0015](0015-sharing-data-items.md) | Sharing data items without breaking need-to-know | Accepted | 2026-10-22 |
| [0016](0016-audit-trail-for-commands-and-data-access.md) | Audit trail for commands and data access | Accepted | 2026-10-22, amended 2026-10-24 |
| [0017](0017-azure-deployment.md) | Deploy the MVP to Azure with GitHub Actions | Accepted | 2026-10-22 |
| [0018](0018-staging-and-production-with-migrations-in-ci-cd.md) | Staging and production environments, and database migrations in CI/CD | Accepted | 2026-10-22 |
| [0019](0019-no-personal-data-in-urls.md) | Personal data stays out of URLs | Accepted | 2026-10-24 |
