# Data Model

## Entities

### User
Represents an individual account.

| Field | Type | Notes |
|---|---|---|
| Id | Guid | Primary key |
| Email | string | Unique, max 256 chars |
| HashedPassword | string | SHA-256 hashed |
| OrgId | Guid? | Foreign key -> Organization |

### Organization
Groups users together.

| Field | Type | Notes |
|---|---|---|
| Id | Guid | Primary key |
| Name | string | Organization name |

### Policy
Defines an access control rule scoped to an organization.

| Field | Type | Notes |
|---|---|---|
| Id | Guid | Primary key |
| Name | string | Policy name |
| OrgId | Guid | Foreign key -> Organization |

### Clearance
Links a user to a policy (grants access).

| Field | Type | Notes |
|---|---|---|
| Id | Guid | Primary key |
| UserId | Guid | Foreign key -> User |
| PolicyId | Guid | Foreign key -> Policy |

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

### Audit_Log
Tracks all significant system actions.

| Field | Type | Notes |
|---|---|---|
| Id | Guid | Primary key |
| UserId | Guid | Foreign key -> User |
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
