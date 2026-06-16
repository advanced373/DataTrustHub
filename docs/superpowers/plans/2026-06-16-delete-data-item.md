# DeleteDataItem (Issue #7) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an owner-only soft-delete endpoint (`DELETE /data/{id}`) to the `DataManagement` feature slice, per the approved spec at `docs/superpowers/specs/2026-06-16-delete-data-item-design.md`.

**Architecture:** Vertical Slice Architecture. New slice `DataTrustHub.Features/DataManagement/DeleteDataItem/` mirrors the existing `AddDataItem` slice exactly (Carter endpoint + MediatR command/handler + FluentValidation validator). Soft delete via a new `IsDeleted` flag on `DbDataItem`.

**Tech Stack:** .NET 9, Carter, MediatR 13, FluentValidation 12, EF Core 9 (InMemory provider in tests), xUnit, `WebApplicationFactory`.

---

### Task 1: Create the feature branch

**Files:** none (git operation only)

- [ ] **Step 1: Create and check out the branch**

```bash
git checkout develop
git pull
git checkout -b feat/7-delete-data
```

Expected: branch `feat/7-delete-data` created from up-to-date `develop`.

---

### Task 2: Add the `IsDeleted` soft-delete flag to `DbDataItem`

**Files:**
- Modify: `DataTrustHub.Infrastructure/Persistance/Model/DbData.cs`

- [ ] **Step 1: Add the property**

Current content:
```csharp
namespace DataTrustHub.Infrastructure.Persistance.Model
{
    public class DbDataItem: DbEntity
    {
        public required string Name { get; set; }
        public required long Size { get; set; }
        public string? Content { get; set; }
        public required Guid OwnerUserId { get; set; }
        public required string SecurityMarking { get; set; }
    }
}
```

New content:
```csharp
namespace DataTrustHub.Infrastructure.Persistance.Model
{
    public class DbDataItem: DbEntity
    {
        public required string Name { get; set; }
        public required long Size { get; set; }
        public string? Content { get; set; }
        public required Guid OwnerUserId { get; set; }
        public required string SecurityMarking { get; set; }
        public bool IsDeleted { get; set; } = false; // NEEDS MIGRATION
    }
}
```

- [ ] **Step 2: Do NOT run `dotnet ef migrations add`**

Per CLAUDE.md Rule 3, schema changes are marked with `// NEEDS MIGRATION` and left for
whoever is designated "Database Owner" for the sprint. Tests use the EF Core InMemory
provider, which builds its model directly from `DbContext`/entity classes and does not
need a migration to run — so this does not block Task 3's tests.

- [ ] **Step 3: Build to confirm the entity still compiles**

Run: `dotnet build DataTrustHub.sln`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add DataTrustHub.Infrastructure/Persistance/Model/DbData.cs
git commit -m "feat: add IsDeleted soft-delete flag to DbDataItem (NEEDS MIGRATION)"
```

---

### Task 3: Write the failing integration tests

**Files:**
- Create: `DataTrustHub.Features.Tests/DataManagement/DeleteDataItemTests.cs`

- [ ] **Step 1: Write the test file**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Helpers;
using Xunit;

namespace DataTrustHub.Features.Tests.DataManagement;

public class DeleteDataItemTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string RegisterEndpoint = "/auth/register";
    private const string LoginEndpoint = "/auth/login";
    private const string UploadEndpoint = "/data/upload";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task DeleteDataItem_Owner_ExistingItem_Returns204()
    {
        var token = await RegisterAndLoginAsync(_client, "delete_owner@example.com", "Password1!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var itemId = await UploadFileAsync(_client, "owner.txt", "hello");

        var response = await _client.DeleteAsync($"/data/{itemId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteDataItem_UnauthenticatedRequest_Returns401()
    {
        var unauthClient = factory.CreateClient();

        var response = await unauthClient.DeleteAsync($"/data/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteDataItem_NonExistentItem_Returns404()
    {
        var token = await RegisterAndLoginAsync(_client, "delete_missing@example.com", "Password1!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.DeleteAsync($"/data/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteDataItem_NonOwner_Returns404()
    {
        var ownerClient = factory.CreateClient();
        var ownerToken = await RegisterAndLoginAsync(ownerClient, "delete_owner2@example.com", "Password1!");
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var itemId = await UploadFileAsync(ownerClient, "secret.txt", "top secret");

        var intruderClient = factory.CreateClient();
        var intruderToken = await RegisterAndLoginAsync(intruderClient, "delete_intruder@example.com", "Password1!");
        intruderClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", intruderToken);

        var response = await intruderClient.DeleteAsync($"/data/{itemId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteDataItem_AlreadyDeleted_Returns404()
    {
        var token = await RegisterAndLoginAsync(_client, "delete_twice@example.com", "Password1!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var itemId = await UploadFileAsync(_client, "ephemeral.txt", "gone soon");

        var firstDelete = await _client.DeleteAsync($"/data/{itemId}");
        Assert.Equal(HttpStatusCode.NoContent, firstDelete.StatusCode);

        var secondDelete = await _client.DeleteAsync($"/data/{itemId}");

        Assert.Equal(HttpStatusCode.NotFound, secondDelete.StatusCode);
    }

    // --- helpers ---

    private static async Task<string> RegisterAndLoginAsync(HttpClient client, string email, string password)
    {
        await client.PostAsJsonAsync(RegisterEndpoint, new { Email = email, Password = password });

        var loginResponse = await client.PostAsJsonAsync(
            LoginEndpoint, new { Email = email, Password = password });

        var login = await loginResponse.Content.ReadFromJsonAsync<LoginUserResponse>();
        return login!.Token;
    }

    private static async Task<Guid> UploadFileAsync(HttpClient client, string fileName, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var fileContentStream = new ByteArrayContent(bytes);
        fileContentStream.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        var form = new MultipartFormDataContent();
        form.Add(fileContentStream, "file", fileName);

        var response = await client.PostAsync(UploadEndpoint, form);
        var result = await response.Content.ReadFromJsonAsync<AddDataItemResponse>();
        return result!.DataItemId;
    }

    private record AddDataItemResponse(Guid DataItemId);
    private record LoginUserResponse(string Token);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DataTrustHub.Features.Tests/ --filter "FullyQualifiedName~DeleteDataItemTests"`
Expected: build succeeds but all 5 tests **FAIL** — there is no `DELETE /data/{id}` route yet,
so every request gets routed to Carter's default 404, which fails the assertions expecting
204/401/404-with-a-real-check (the 404 tests may incidentally pass for the wrong reason —
that's fine, the next task makes them pass for the *right* reason).

- [ ] **Step 3: Commit the test file**

```bash
git add DataTrustHub.Features.Tests/DataManagement/DeleteDataItemTests.cs
git commit -m "test: add failing integration tests for DeleteDataItem"
```

---

### Task 4: Implement the DeleteDataItem slice

**Files:**
- Create: `DataTrustHub.Features/DataManagement/DeleteDataItem/Constants.cs`
- Create: `DataTrustHub.Features/DataManagement/DeleteDataItem/DeleteDataItemHandler.cs`
- Create: `DataTrustHub.Features/DataManagement/DeleteDataItem/DeleteDataItemValidator.cs`
- Create: `DataTrustHub.Features/DataManagement/DeleteDataItem/DeleteDataItemEndpoint.cs`

- [ ] **Step 1: Create `Constants.cs`**

```csharp
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.DataManagement.DeleteDataItem;

internal static class Constants
{
    internal const string EndpointRoute = "/data/{id:guid}";
    internal const string EndpointTag = "DataManagement";
}

internal static class Errors
{
    internal static readonly Error DataItemNotFound = Error.NotFound(
        "DataItem.NotFound",
        "The data item was not found.");
}
```

- [ ] **Step 2: Create `DeleteDataItemHandler.cs`** (command + handler)

```csharp
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.DataManagement.DeleteDataItem;

public record DeleteDataItemCommand(Guid DataItemId, Guid RequesterId) : IRequest<Result<Guid>>;

public class DeleteDataItemHandler(DContext db) : IRequestHandler<DeleteDataItemCommand, Result<Guid>>
{
    private readonly DContext _db = db;

    public async Task<Result<Guid>> Handle(
        DeleteDataItemCommand command,
        CancellationToken cancellationToken)
    {
        var dataItem = await _db.DataItems.FirstOrDefaultAsync(
            d => d.Id == command.DataItemId && !d.IsDeleted,
            cancellationToken);

        if (dataItem is null || dataItem.OwnerUserId != command.RequesterId)
            return Result.Failure<Guid>(Errors.DataItemNotFound);

        dataItem.IsDeleted = true;
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(command.DataItemId);
    }
}
```

- [ ] **Step 3: Create `DeleteDataItemValidator.cs`**

```csharp
using FluentValidation;

namespace DataTrustHub.Features.DataManagement.DeleteDataItem;

internal class DeleteDataItemValidator : AbstractValidator<DeleteDataItemCommand>
{
    public DeleteDataItemValidator()
    {
        RuleFor(x => x.DataItemId).NotEmpty();
        RuleFor(x => x.RequesterId).NotEmpty();
    }
}
```

- [ ] **Step 4: Create `DeleteDataItemEndpoint.cs`**

```csharp
using Carter;
using DataTrustHub.Features._Shared.Extensions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace DataTrustHub.Features.DataManagement.DeleteDataItem;

public class DeleteDataItemEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapDelete(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .RequireAuthorization();

    private static async Task<IResult> Handle(
        Guid id,
        ISender sender,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userIdResult = ExtractUserId(httpContext.User);
        if (userIdResult is null) return Results.Unauthorized();

        var command = new DeleteDataItemCommand(id, userIdResult.Value);
        var result = await sender.Send(command, cancellationToken);
        return result.ToHttpResult(_ => Results.NoContent());
    }

    private static Guid? ExtractUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
               ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(sub, out var id) ? id : null;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test DataTrustHub.Features.Tests/ --filter "FullyQualifiedName~DeleteDataItemTests"`
Expected: `Passed! - Failed: 0, Passed: 5, Skipped: 0`

- [ ] **Step 6: Commit**

```bash
git add DataTrustHub.Features/DataManagement/DeleteDataItem/
git commit -m "feat: implement DeleteDataItem slice (owner-only soft delete)"
```

---

### Task 5: Full regression run

**Files:** none (verification only)

- [ ] **Step 1: Run the entire test suite**

Run: `dotnet test DataTrustHub.Features.Tests/`
Expected: all existing suites (Authentication, Administration, AddDataItem, DeleteDataItem)
pass — `Failed: 0`.

- [ ] **Step 2: Build the full solution**

Run: `dotnet build DataTrustHub.sln`
Expected: `Build succeeded.`

- [ ] **Step 3: Update the GitHub Project board**

Issue #7 is currently "In progress" (set earlier in this session). Once tests and build
pass, move it to "Done":

```bash
gh project item-edit --project-id PVT_kwHOAyuapc4BFjHG --id PVTI_lAHOAyuapc4BFjHGzgf7uzk \
  --field-id PVTSSF_lAHOAyuapc4BFjHGzg22mlU --single-select-option-id 98236657
```

No commit needed for this step (it only changes board state on GitHub, not files).

---

## Out of scope (per spec section 7)

Audit logging, restore/undelete, and hard delete/purge are explicitly not part of this plan.
