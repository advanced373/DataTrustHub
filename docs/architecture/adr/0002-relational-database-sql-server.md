# ADR-0002: Keep a relational database (SQL Server)

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** #26

## Context

Whether to move from SQL Server to a NoSQL store came up. The data is strongly relational:
users belong to organizations, clearances link users to policies, and data items have an owner
and a classification level. Every access check joins several of these tables, and a revoked
clearance must take effect immediately.

## Decision

Keep SQL Server, accessed through EF Core 9 (`DContext`). The metadata and the access-control
model stay relational, with foreign keys and transactions.

Large file content is the part that does not fit well. It should move out of `DbDataItem.Content`
into blob storage, keeping only a storage key in SQL Server (proposed in #26).

## Alternatives considered

- **NoSQL as the main store.** Denormalized copies of permissions would have to be kept in
  sync by hand, a classic source of access-control bugs. The data volume does not need
  horizontal scaling.
- **NoSQL next to SQL Server.** Still an option for specific needs (high-volume audit log,
  full-text search), decided case by case.

## Consequences

- ACID transactions and foreign keys guarantee consistency of access rules.
- Schema changes need EF migrations (see [ADR-0003](0003-single-database-owner-for-migrations.md)).
- Until #26 is done, file content bloats the database.
