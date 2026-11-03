# Data Model

## Entities

### User
Represents an individual account.

| Field | Type | Notes |
|---|---|---|
| Id | Guid | Primary key |
| Email | string | nvarchar(256), normalized (U7). Unique index `IX_Users_Email` (U1) |
| HashedPassword | string | SHA-256 hashed |
| OrgId | Guid? | Foreign key -> Organization |

### Organization
Groups users together.

| Field | Type | Notes |
|---|---|---|
| Id | Guid | Primary key |
| Name | string | Organization name, nvarchar(256). Unique index `IX_Organizations_Name` (O1) |

### Policy
Defines an access control rule scoped to an organization.

| Field | Type | Notes |
|---|---|---|
| Id | Guid | Primary key |
| Name | string | Policy name, nvarchar(256). Unique per organization: index `IX_Policies_OrganizationId_Name` (P2) |
| OrgId | Guid | Foreign key -> Organization |

### Clearance
Links a user to a policy (grants access).

| Field | Type | Notes |
|---|---|---|
| Id | Guid | Primary key |
| UserId | Guid | References a User by id (no foreign key) |
| PolicyId | Guid | References a Policy by id (no foreign key). (`UserId`, `PolicyId`) is unique: index `IX_Clearances_UserId_PolicyId` (C3) |

### DataItem
A file or data object with a security classification.

| Field | Type | Notes |
|---|---|---|
| Id | Guid | Primary key |
| Name | string | Display name |
| FileSize | long | Size in bytes |
| Content | byte[] | File content |
| OwnerUserId | Guid | Foreign key -> User |
| ClassificationLevel | enum | Security level |
| PolicyId | Guid? | The policy of a classified item; empty for a personal item |
| ClassificationPriority | int? | Priority of the item's classification level; empty for a personal item. Check constraint `CK_DataItems_Classification` (D1): `PolicyId` and `ClassificationPriority` are both empty or both set |

### Audit_Log
Tracks all significant system actions.

| Field | Type | Notes |
|---|---|---|
| Id | Guid | Primary key |
| UserId | Guid | References a User by id (no foreign key) |
| EventType | string | Category of event |
| EventDetails | string | Free-text details |

## Key relationships

- User belongs to Organization (via OrgId)
- Clearance connects User to Policy (many-to-many bridge)
- Policy is scoped to Organization
- DataItem is owned by User
- Audit_Log records every action performed by a User

## DB access in slices

Inject DContext directly — it is registered in DI by Infrastructure:

```csharp
public class MyHandler(DContext db) : IRequestHandler<MyCommand, Result<Guid>>
{
    private readonly DContext _db = db;
}
```
