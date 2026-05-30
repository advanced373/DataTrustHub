# DataTrustHub — VSA Migration + Multi-Agent Architecture Design

**Date:** 2026-05-30  
**Status:** Approved  
**Author:** Brainstorming session with Claude Code

---

## 1. Context

DataTrustHub este o platformă de management securizat al datelor cu control acces bazat pe politici. Proiectul folosește .NET 9 și urmează Clean Architecture (SharedKernel, Domain, Application, Infrastructure, API).

**Problema:** Arhitectura actuală nu permite agenților Claude Code să lucreze independent pe task-uri GitHub fără riscul de overlap sau conflicte. Fiecare layer (Application, Domain) este cross-cutting, ceea ce înseamnă că orice agent care lucrează pe un feature trebuie să atingă mai multe proiecte simultan.

**Soluția:** Migrare la Vertical Slice Architecture (VSA) + un proiect nou `DataTrustHub.Features` + workflow de agenți paraleli bazat pe git worktrees.

---

## 2. Arhitectura VSA

### 2.1 Principiu

Codul este organizat după **feature**, nu după **layer**. Fiecare feature (slice) conține tot ce are nevoie: endpoint, handler, validator, DTOs, entități locale. Nu există dependențe între feature foldere.

### 2.2 Structura proiectului nou: `DataTrustHub.Features`

```
DataTrustHub.Features/
  _Shared/
    Database/
      AppDbContext.cs            ← DbContext partajat (read-only pentru agenți)
      Migrations/
    Extensions/
      SliceRegistration.cs      ← Înregistrare automată slice-uri via reflection
  
  Authentication/               ← Task GitHub #1
    RegisterUser/
      RegisterUserEndpoint.cs
      RegisterUserHandler.cs
      RegisterUserValidator.cs
    LoginUser/
      LoginUserEndpoint.cs
      LoginUserHandler.cs
  
  DataManagement/               ← Task GitHub #2 + #5
    AddDataItem/
      AddDataItemEndpoint.cs
      AddDataItemHandler.cs
      AddDataItemValidator.cs
    ViewDataItems/
      ViewDataItemsEndpoint.cs
      ViewDataItemsHandler.cs
  
  Sharing/                      ← Task GitHub #3
    ShareData/
      ShareDataEndpoint.cs
      ShareDataHandler.cs
      ShareDataValidator.cs
  
  Administration/               ← Task GitHub #4
    ManageOrganizations/
      ...
    ManagePolicies/
      ...
    ManageClearances/
      ...
  
  MainView/                     ← Task GitHub #16
    GetDashboard/
      GetDashboardEndpoint.cs
      GetDashboardHandler.cs
```

### 2.3 Structura unui slice individual

Fiecare use case respectă același pattern:

```csharp
// Features/Authentication/RegisterUser/RegisterUserEndpoint.cs

// --- DTOs ---
public record RegisterUserRequest(string Email, string Password);
public record RegisterUserResponse(Guid UserId);

// --- Endpoint (Minimal API) ---
public class RegisterUserEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/auth/register", Handle)
           .WithTags("Authentication");

    private static async Task<IResult> Handle(
        RegisterUserRequest req, ISender sender, CancellationToken ct)
    {
        var command = new RegisterUserCommand(req.Email, req.Password);
        var result = await sender.Send(command, ct);
        return result.IsSuccess
            ? Results.Ok(new RegisterUserResponse(result.Value))
            : Results.BadRequest(result.Error);
    }
}

// --- Command + Handler ---
public record RegisterUserCommand(string Email, string Password)
    : IRequest<Result<Guid>>;

public class RegisterUserHandler : IRequestHandler<RegisterUserCommand, Result<Guid>>
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;

    public RegisterUserHandler(AppDbContext db, IPasswordHasher hasher)
        => (_db, _hasher) = (db, hasher);

    public async Task<Result<Guid>> Handle(
        RegisterUserCommand cmd, CancellationToken ct) { ... }
}

// --- Validator ---
public class RegisterUserValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
    }
}
```

### 2.4 Strategia de migrare din Clean Architecture

Migrarea se face feature cu feature, nu big-bang:

| Pas | Acțiune |
|---|---|
| 1 | Creare proiect `DataTrustHub.Features` cu `_Shared/` |
| 2 | Configurare `Program.cs` să înregistreze slice-uri via Carter/reflection |
| 3 | Migrare `Authentication` — primul slice complet ca model |
| 4 | Migrare `DataManagement`, `Sharing`, `Administration`, `MainView` |
| 5 | Dezactivare treptat a `DataTrustHub.Application` și `DataTrustHub.API` vechi |
| 6 | `DataTrustHub.Domain` și `DataTrustHub.Infrastructure` rămân, se curăță ce nu mai e folosit |
| 7 | `DataTrustHub.WebApp` (Razor/MVC frontend) rămâne neatins în această fază — migrarea UI este un epic separat |

**Regula migrației:** Niciun slice nou nu se scrie în proiectele vechi (Application, Domain). Toate feature-urile noi merg direct în `DataTrustHub.Features`.

### 2.5 Tratarea conflictelor la migrații DB

Migrațiile EF Core sunt singura zonă de potențial conflict între agenți. Regula:

- Agentul definește entitățile și marchează cu `// NEEDS MIGRATION`
- Orchestratorul (dezvoltatorul) desemnează explicit un "Database Owner" înainte de fiecare sprint prin mesaj direct agentului respectiv
- Database Owner-ul rulează `Add-Migration` și aplică toate migrațiile marcate `// NEEDS MIGRATION`
- Niciun alt agent nu rulează migrații fără această desemnare explicită

---

## 3. Workflow Multi-Agent

### 3.1 Principiu

```
Un agent Claude Code = Un task GitHub = Un git worktree = Un feature folder
```

Agenții nu comunică între ei și nu partajează stare. Izolarea este garantată prin structura folderelor și regulile din CLAUDE.md.

### 3.2 Maparea task-urilor

| GitHub Issue | Feature Folder | Branch | Sprint |
|---|---|---|---|
| #1 Autentification | `Features/Authentication/` | `feat/1-authentication` | 1 |
| #16 Build basic structure for main view | `Features/MainView/` | `feat/16-main-view` | 1 |
| #2 Add data | `Features/DataManagement/` | `feat/2-add-data` | 2 |
| #5 View data | `Features/DataManagement/` | `feat/5-view-data` | 2 |
| #4 Administration of data | `Features/Administration/` | `feat/4-administration` | 2 |
| #3 Share data | `Features/Sharing/` | `feat/3-share-data` | 3 (depinde de #2, #4) |

### 3.3 Fluxul de lucru per agent

```bash
# 1. Orchestratorul (dezvoltatorul) creează worktree-ul
git worktree add worktrees/agent-auth -b feat/1-authentication

# 2. Deschide Claude Code în acel worktree
cd worktrees/agent-auth
claude  # Agentul citește automat CLAUDE.md

# 3. Agentul lucrează exclusiv în Features/Authentication/
# 4. Agentul deschide PR la final
# 5. Orchestratorul face review + merge
# 6. Worktree cleanup
git worktree remove worktrees/agent-auth
```

### 3.4 Reguli de izolare (enforced via CLAUDE.md)

**Permis:**
- Creare/modificare fișiere în `Features/<FeatureName>/`
- Adăugare entități în `_Shared/Database/` cu marcaj `// NEEDS MIGRATION`
- Import din `DataTrustHub.SharedKernel` (Result, Error, Entity)
- Inject `AppDbContext` din `DataTrustHub.Infrastructure`
- Citire din orice folder (read-only)

**Interzis:**
- Modificarea fișierelor din alt feature folder
- Modificarea `Program.cs`, `appsettings.json`, infrastructurii comune fără aprobare
- Import direct între feature foldere (ex: `using DataTrustHub.Features.Authentication` din `DataManagement`)
- Rularea `Add-Migration` fără coordonare

### 3.5 Ordinea recomandată de lansare a agenților

Unele feature-uri au dependențe logice (nu de cod):

```
Sprint 1 (paralel):
  Agent 1 → #1 Authentication (User entity, JWT)
  Agent 2 → #16 Main view structure (UI skeleton, routing)

Sprint 2 (paralel, după merge Sprint 1):
  Agent 3 → #2 Add data
  Agent 4 → #5 View data
  Agent 5 → #4 Administration

Sprint 3:
  Agent 6 → #3 Share data (depinde de #2 + #4)
```

---

## 4. Documentația

### 4.1 Structura fișierelor de documentație

```
docs/
  architecture/
    overview.md          ← Diagrama arhitecturii, decizii ADR
    vertical-slices.md   ← Ghid VSA: cum se scrie un slice, exemple
    data-model.md        ← Entități, relații, schema DB
  agents/
    workflow.md          ← Cum se pornesc agenții, git worktrees pas-cu-pas
    task-mapping.md      ← GitHub issue → folder → branch
    isolation-rules.md   ← Ce poate/nu poate face un agent
  api/
    endpoints.md         ← Lista completă endpoints (generată din OpenAPI)
```

### 4.2 CLAUDE.md (rădăcina repo-ului)

CLAUDE.md este citit automat de fiecare agent la pornire. Conține:
- Descrierea proiectului și stack-ul tehnic
- Regulile de izolare VSA
- Ce poate și ce nu poate face agentul
- Referințe la documentația tehnică

---

## 5. Livrabile concrete

| Livrabil | Locație | Descriere |
|---|---|---|
| `CLAUDE.md` | `/` | Context automat pentru orice agent |
| `docs/architecture/overview.md` | `/docs/architecture/` | Arhitectura VSA explicată |
| `docs/architecture/data-model.md` | `/docs/architecture/` | Entități și relații |
| `docs/agents/workflow.md` | `/docs/agents/` | Ghid pas-cu-pas agenți paraleli |
| `docs/agents/task-mapping.md` | `/docs/agents/` | Mapare GitHub → folder → branch |
| `DataTrustHub.Features/` | `/` | Proiect nou cu structura VSA |

---

## 6. Code Style Rules

All code written in `DataTrustHub.Features` (and across the entire solution) must follow these rules. Agents must apply them without exception.

### 6.1 Naming

- **camelCase** for local variables and parameters: `userId`, `dataItemList`, `hashedPassword`
- **PascalCase** for types, methods, properties, and constants: `RegisterUserHandler`, `MaxPasswordLength`
- **No abbreviations** unless universally known (`id`, `dto`, `db` are acceptable; `usr`, `mgr`, `svc` are not)

### 6.2 Language

- All **comments must be written in English**
- All **documentation, specs, and ADRs must be written in English**
- Commit messages in English

### 6.3 Constants — No Magic Strings or Numbers

Every string literal or numeric value used as a configuration or rule must be extracted to a named constant. Inline literals in logic are forbidden.

```csharp
// BAD
if (password.Length < 8) ...
app.MapPost("/auth/register", Handle);

// GOOD
private const int MinPasswordLength = 8;
private const string RegisterEndpoint = "/auth/register";

if (password.Length < MinPasswordLength) ...
app.MapPost(RegisterEndpoint, Handle);
```

Constants live in a `Constants.cs` file inside the slice folder, or in `_Shared/Constants/` if shared across slices.

### 6.4 Function Length — Maximum 30 Lines

No method body may exceed 30 lines (excluding blank lines and braces). If a method grows beyond this, extract private helper methods.

```csharp
// BAD — one long Handle method doing validation + business logic + mapping
public async Task<Result<Guid>> Handle(RegisterUserCommand cmd, CancellationToken ct)
{
    // 40 lines of mixed concerns
}

// GOOD — each concern is its own method
public async Task<Result<Guid>> Handle(RegisterUserCommand cmd, CancellationToken ct)
{
    var validationResult = await ValidateUserDoesNotExist(cmd.Email, ct);
    if (validationResult.IsFailure) return validationResult;

    var user = CreateUser(cmd);
    await PersistUser(user, ct);
    return Result.Success(user.Id);
}

private async Task<Result> ValidateUserDoesNotExist(string email, CancellationToken ct) { ... }
private User CreateUser(RegisterUserCommand cmd) { ... }
private async Task PersistUser(User user, CancellationToken ct) { ... }
```

### 6.5 Abstraction

- **Depend on interfaces, not concrete types.** Services injected into handlers must be defined as interfaces.
- **No direct instantiation** of services inside handlers (`new SomeService()` is forbidden — use DI).
- Infrastructure concerns (email sending, file storage, external APIs) must be behind an interface defined in the slice or in `_Shared/Abstractions/`.

```csharp
// BAD
public class ShareDataHandler
{
    private readonly EmailService _emailService = new EmailService(); // forbidden
}

// GOOD
public class ShareDataHandler
{
    private readonly IEmailNotifier _emailNotifier;
    public ShareDataHandler(IEmailNotifier emailNotifier) => _emailNotifier = emailNotifier;
}
```

### 6.6 SOLID Principles

| Principle | Rule |
|---|---|
| **Single Responsibility** | Each class has one reason to change. Handlers handle; validators validate; endpoints route. Never mix. |
| **Open/Closed** | Extend behavior via new classes or interfaces, not by modifying existing handlers. |
| **Liskov Substitution** | Implementations must be substitutable for their interfaces without altering correctness. |
| **Interface Segregation** | Define narrow interfaces per use case. Avoid fat interfaces with methods unrelated to the caller. |
| **Dependency Inversion** | Handlers depend on abstractions (`IRepository`, `IEmailNotifier`), never on concrete infrastructure types. |

### 6.7 Summary Checklist (per slice, before PR)

- [ ] All comments in English
- [ ] No inline string or numeric literals in logic
- [ ] No method exceeds 30 lines
- [ ] All injected dependencies are interfaces
- [ ] Each class has a single responsibility
- [ ] Constants extracted to `Constants.cs`

---

## 7. Decizii arhitecturale (ADR)

### ADR-001: Separate project vs folder inside API
**Decision:** Separate project `DataTrustHub.Features`  
**Reason:** Clear compile-time isolation; agents cannot accidentally import from the wrong project; gradual migration without destabilizing the existing API.

### ADR-002: Carter for Minimal API endpoints
**Decision:** Use Carter (`ICarterModule`) for endpoint registration  
**Reason:** VSA-friendly pattern, auto-discovery via reflection, eliminates boilerplate from `Program.cs`.  
**Required action:** Add NuGet `Carter` to `DataTrustHub.Features.csproj` — not present in the solution yet.

### ADR-003: MediatR stays inside slices
**Decision:** MediatR continues as the internal mediator within each slice  
**Reason:** Already present in the project; agents are familiar with it.  
**Note:** `ValidationPipelineBehavior` and `AddValidatorsFromAssembly` are commented out in `DependencyInjection.cs` — enable them from the start in the `Features` project.

### ADR-004: Centralized migrations
**Decision:** A single "Database Owner" per sprint applies all migrations  
**Reason:** EF Core does not support parallel migrations on the same context without timestamp and snapshot conflicts.

### ADR-005: English as the project language
**Decision:** All code artifacts (comments, specs, docs, commit messages, constant names) are written in English  
**Reason:** Consistency across agents; English is the universal default for technical artifacts and avoids encoding issues with diacritics in identifiers.
