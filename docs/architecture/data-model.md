# Data Model

The SQL Server tables, as mapped by the persistence models in
`DataTrustHub.Infrastructure/Persistance/Model` and the migrations next to them. The business rules
named in brackets (U1, D1, …) are described in the [domain handbook](../domain/aggregates.md).

Aggregates reference each other by id, without foreign keys (ADR-0012). The only foreign keys are inside
the classification-level table, listed under [Relationships](#relationships).

## Tables

### Users

| Column | Type | Notes |
|---|---|---|
| Id | uniqueidentifier | Primary key |
| Email | nvarchar(256) | Stored normalized (U7). Unique index `IX_Users_Email` (U1) |
| HashedPassword | nvarchar(max) | Password hash, produced by `IPasswordHasher` |
| Role | nvarchar(max) | Platform role: `User` or `Admin` (ADR-0006) |

### Organizations

| Column | Type | Notes |
|---|---|---|
| Id | uniqueidentifier | Primary key |
| Name | nvarchar(256) | Unique index `IX_Organizations_Name` (O1) |

### Memberships

A user's membership of an organization (ADR-0014).

| Column | Type | Notes |
|---|---|---|
| Id | uniqueidentifier | Primary key |
| UserId | uniqueidentifier | References a user by id |
| OrganizationId | uniqueidentifier | References an organization by id |
| Role | nvarchar(32) | `Member`, `Guest` or `OrgAdmin`. (`UserId`, `OrganizationId`) is unique: index `IX_Memberships_UserId_OrganizationId` (M1) |

### Policies

An access policy of an organization.

| Column | Type | Notes |
|---|---|---|
| Id | uniqueidentifier | Primary key |
| Name | nvarchar(256) | Unique per organization: index `IX_Policies_OrganizationId_Name` (P2) |
| OrganizationId | uniqueidentifier | References an organization by id |

### ClassificationLevels

The levels of a policy, ordered by priority.

| Column | Type | Notes |
|---|---|---|
| Id | int | Primary key (identity) |
| Name | nvarchar(max) | Level name, also used as a data item's security marking |
| Priority | int | Higher is more restricted |
| DbPolicyId | uniqueidentifier, null | Foreign key to `Policies` |

### Clearances

Grants a user access to a policy up to a classification level.

| Column | Type | Notes |
|---|---|---|
| Id | uniqueidentifier | Primary key |
| Name | nvarchar(max) | Display name |
| UserId | uniqueidentifier | References a user by id |
| PolicyId | uniqueidentifier | References a policy by id. (`UserId`, `PolicyId`) is unique: index `IX_Clearances_UserId_PolicyId` (C3) |
| ClassificationLevelId | int | Foreign key to `ClassificationLevels`: the highest level the clearance allows |

### DataItems

A stored file, personal or classified (ADR-0011).

| Column | Type | Notes |
|---|---|---|
| Id | uniqueidentifier | Primary key |
| Name | nvarchar(max) | File name |
| Size | bigint | Size in bytes |
| Content | nvarchar(max), null | File content, base64-encoded (#26 plans blob storage) |
| OwnerUserId | uniqueidentifier | References the uploading user by id |
| SecurityMarking | nvarchar(max) | `UNCLASSIFIED` for a personal item, otherwise the level's name |
| IsDeleted | bit | Soft delete by the owner (ADR-0004) |
| PolicyId | uniqueidentifier, null | The policy of a classified item; empty for a personal item |
| ClassificationPriority | int, null | Priority of the item's level; empty for a personal item |

Check constraint `CK_DataItems_Classification` (D1): `PolicyId` and `ClassificationPriority` are both
empty or both set.

### AuditRecords

The append-only audit trail (ADR-0016). A record outlives what it names, so it has no foreign keys.

| Column | Type | Notes |
|---|---|---|
| Id | uniqueidentifier | Primary key |
| OccurredAt | datetime2 | Indexed, alone and with `OrganizationId` |
| ActorUserId | uniqueidentifier, null | The signed-in user; empty for an anonymous request |
| Action | nvarchar(128) | The use case |
| TargetType | nvarchar(64), null | The kind of target |
| TargetId | uniqueidentifier, null | The target's id |
| OrganizationId | uniqueidentifier, null | The organization the action belongs to, if any |
| Outcome | nvarchar(16) | The name of an `AuditOutcome` |
| ErrorCode | nvarchar(256), null | The error code of a failed action |

## Relationships

- A user belongs to organizations through **Memberships**, one per organization (M1).
- A policy belongs to an organization and owns its **ClassificationLevels** (foreign key `DbPolicyId`).
- A **Clearance** gives a user access to a policy up to a level (foreign key `ClassificationLevelId`).
- A **DataItem** is owned by a user. A classified item belongs to a policy and a level priority; it is read
  by holders of a clearance on that policy at or above that priority (ADR-0011).
- An **AuditRecord** names its actor, target and organization by id only.

## Database access

- **Commands** go through the Domain ports (`I*Repository`, `IUnitOfWork`), implemented in Infrastructure.
- **Queries** read `DContext` with `AsNoTracking` until ADR-0012 step 4 (#74).
- Only the Database Owner adds migrations (ADR-0003); CI/CD applies them (ADR-0018).
