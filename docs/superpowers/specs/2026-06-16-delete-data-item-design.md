# DataTrustHub — Delete Data Item (Issue #7) Design

**Date:** 2026-06-16
**Status:** Approved
**Author:** Brainstorming session with Claude Code

---

## 1. Context

GitHub issue #7 ("Delete data") a fost implementat inițial înainte de migrarea la Vertical
Slice Architecture (vezi `2026-05-30-datatrusthub-vsa-multiagent-design.md`) și a fost
închis pe GitHub, dar codul lui nu există în `DataTrustHub.Features/`. Implementarea veche
(`DataTrustHub.Application/Data/Delete/`) avea o gaură de securitate: orice user autentificat
putea șterge orice `DataItem` după Id, fără verificare de owner.

**Scop:** implementa slice-ul `DeleteDataItem` în `DataTrustHub.Features/DataManagement/`,
urmând exact șablonul `AddDataItem` (singurul slice existent în acel feature folder),
și corectând gaura de securitate identificată.

---

## 2. Decizii de design

| Decizie | Alegere | Motivație |
|---|---|---|
| Cine poate șterge | Doar owner-ul (`OwnerUserId == RequesterId`) | Corectează gaura de securitate din implementarea veche; relevant într-o platformă cu control acces bazat pe politici |
| Tip de delete | Soft delete (flag `IsDeleted`) | Permite recuperare/audit ulterior; nu există încă alt pattern de soft-delete în bază, dar nu există nicio constrângere care să-l excludă |
| Numele flag-ului | `IsDeleted` (bool, default `false`) | Convenție EF Core standard |
| Cod HTTP pentru non-owner | 404 (NotFound), identic cu item inexistent | Evită scurgerea informației că un item al altcuiva există; nu introduce un `ErrorType.Forbidden` nou în `SharedKernel` (ar necesita aprobare separată pentru infra, vezi Rule 1/3 din CLAUDE.md) |
| Mapare succes → HTTP | Handler-ul returnează `Result<Guid>` (id-ul șters), endpoint-ul mapează la `Results.NoContent()` | Reutilizează `ToHttpResult<T>` din `_Shared/Extensions/ResultExtensions.cs`, care e read-only pentru agenți — evită orice modificare în `_Shared` |
| Ștergere dublă (item deja `IsDeleted`) | 404, tratat ca inexistent | Idempotent din perspectiva apelantului; simplifică query-ul la un singur filtru `IsDeleted == false` |

---

## 3. Schema

`DbDataItem` (`DataTrustHub.Infrastructure/Persistance/Model/DbData.cs`) primește o coloană nouă:

```csharp
public bool IsDeleted { get; set; } = false; // NEEDS MIGRATION
```

Per Rule 3 din CLAUDE.md, agentul marchează linia cu `// NEEDS MIGRATION` și **nu** rulează
`dotnet ef migrations add` fără ca orchestratorul să-l desemneze "Database Owner".

---

## 4. Componente (`DataTrustHub.Features/DataManagement/DeleteDataItem/`)

- **`Constants.cs`** — `EndpointRoute = "/data/{id}"`, `EndpointTag = "DataManagement"`, definiții `Errors.DataItemNotFound`.
- **`DeleteDataItemEndpoint.cs`** (`ICarterModule`) — `MapDelete(Constants.EndpointRoute, Handle).RequireAuthorization()`. Extrage `userId` din JWT (identic cu `AddDataItemEndpoint.ExtractUserId`), construiește `DeleteDataItemCommand(id, userId)`, trimite prin `ISender`, mapează rezultatul cu `ToHttpResult(_ => Results.NoContent())`.
- **`DeleteDataItemHandler.cs`** (`IRequestHandler<DeleteDataItemCommand, Result<Guid>>`):
  1. Caută `DbDataItem` cu `Id == request.DataItemId && IsDeleted == false`.
  2. Dacă nu există sau `OwnerUserId != request.RequesterId` → `Result.Failure<Guid>(Errors.DataItemNotFound)`.
  3. Altfel: setează `IsDeleted = true`, `SaveChangesAsync`, returnează `Result.Success(request.DataItemId)`.
- **`DeleteDataItemValidator.cs`** (`AbstractValidator<DeleteDataItemCommand>`) — `RuleFor(x => x.DataItemId).NotEmpty()`; `RuleFor(x => x.RequesterId).NotEmpty()`.

**Command:** `public record DeleteDataItemCommand(Guid DataItemId, Guid RequesterId) : IRequest<Result<Guid>>;`

---

## 5. Erori

```csharp
internal static class Errors
{
    internal static readonly Error DataItemNotFound = Error.NotFound(
        "DataItem.NotFound",
        "The data item was not found.");
}
```

(Codul `DataItem.NotFound` e folosit identic pentru "nu există" și "nu ești owner" — vezi decizia din secțiunea 2.)

---

## 6. Testare (`DataTrustHub.Features.Tests/DataManagement/DeleteDataItemTests.cs`)

Urmează șablonul `AddDataItemTests.cs` (`ApiFactory`, `RegisterAndLoginAsync` helper):

1. **Owner șterge propriul item** → `204 No Content`. Verificare suplimentară: un upload nou cu același owner urmat de delete, apoi (dacă există) un GET ar trebui să nu mai returneze item-ul — sau verificare directă în `DContext` că `IsDeleted == true`.
2. **Non-owner încearcă să șteargă item-ul altcuiva** → `404 NotFound`.
3. **Item inexistent (Guid random)** → `404 NotFound`.
4. **Request neautentificat** → `401 Unauthorized`.
5. **Ștergere dublă** (același item, al doilea apel) → `404 NotFound`.

---

## 7. Ce rămâne în afara scopului

- Audit logging la delete — pipeline-ul de audit din arhitectura veche (#6) nu a fost
  reportat în VSA; nu se adaugă aici pentru a evita scope creep.
- Restore/undelete — nu a fost cerut de issue.
- Hard delete / purge — nu e cerut acum; poate fi un task separat ulterior.
