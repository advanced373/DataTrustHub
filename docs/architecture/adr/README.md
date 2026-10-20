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
