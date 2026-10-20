# ADR-0010: Port the legacy endpoints to VSA slices and remove the layered code

- **Status:** Accepted
- **Date:** 2026-10-20
- **Supersedes:** [ADR-0008](0008-secure-legacy-controllers-until-replaced.md)
- **Related:** #25, #41, #50–#57

## Context

Two architectures ran side by side in the API: the legacy layered code (`DataTrustHub.Application`,
the controllers in `DataTrustHub.API/Controllers`, repositories over Domain entities) and the VSA
slices in `DataTrustHub.Features` ([ADR-0001](0001-vertical-slice-architecture.md)).
This duplicated concepts, such as two validation pipelines and two models for each entity, and the
two models had already drifted: the Domain `User` had no `Role`, and the Domain `DataItem` had no `IsDeleted`.

[ADR-0008](0008-secure-legacy-controllers-until-replaced.md) chose to add authorization to the
legacy controllers until VSA slices replaced them. An inventory for #25 then showed that:
- no client uses the legacy routes, since the WebApp calls only VSA routes;
- the missing VSA endpoints (listing and deleting users, groups and policies, managing clearances,
  reading data items) are small slices that #5 and #23 need anyway.

## Decision

Port every legacy endpoint that has no VSA equivalent as an authorized slice, then delete the legacy code.

1. **Port.** The new slices are owner-only for data items and `Admin` only for administration
   (#50–#55). Deletes return `409` while dependents exist (a group's policies, a policy's clearances),
   because these references have no foreign keys.
2. **Delete.** Remove the controllers, the API DTOs, the `Application` project and the repositories (#56).
   Then remove the Domain types that only they used (#57).

`DataTrustHub.Domain` keeps only `IPasswordHasher`. Whether to drop the project or grow it into a
real domain model is left open.

## Alternatives considered

- **Secure the legacy controllers ([ADR-0008](0008-secure-legacy-controllers-until-replaced.md), #41).**
  It means writing authorization code for code that is going to be deleted, and it keeps the
  hybrid architecture.
- **Delete the legacy code first, port later.** Listing and clearance management would be missing
  until #23. It was rejected for this reason earlier (#34, #35).

## Consequences

- One architecture: every endpoint is a VSA slice, and `dotnet test` enforces the slice rules (#49).
- The solution drops from 8 projects to 7.
- #41 is no longer needed and is closed once #56 is merged.
- Routes changed from `/User`, `/DataItem`, `/Organization`, `/Policy` and `/Clearance` to
  `/auth/*`, `/data/*` and `/admin/*`. No known client used the old ones.
