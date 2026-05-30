# VSA Migration + Multi-Agent Setup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate the DataTrustHub backend to Vertical Slice Architecture, implement the Authentication slice as a reusable model, and produce documentation + CLAUDE.md so parallel agents can work independently on feature tasks.

**Architecture:** New project `DataTrustHub.Features` holds all feature slices. Each slice is fully autonomous: one folder per use case containing the endpoint, handler, validator, and constants. Slices communicate only through the shared `DContext` DbContext and `DataTrustHub.SharedKernel` types. Carter auto-discovers endpoints; MediatR with `ValidationPipelineBehavior` routes commands through validators.

**Tech Stack:** .NET 9, ASP.NET Core Minimal API, Carter 8.x, MediatR 13, FluentValidation 12, EF Core 9 (SQL Server + InMemory for tests), xUnit, Microsoft.AspNetCore.Mvc.Testing, Microsoft.AspNetCore.Authentication.JwtBearer 9.x

---

## File Map

### New files
| File | Responsibility |
|---|---|
| `DataTrustHub.Features/DataTrustHub.Features.csproj` | Project definition with Carter, MediatR, FluentValidation, JwtBearer |
| `DataTrustHub.Features.Tests/DataTrustHub.Features.Tests.csproj` | Test project with xUnit, Mvc.Testing, EF InMemory |
| `DataTrustHub.Features.Tests/Helpers/ApiFactory.cs` | WebApplicationFactory with InMemory DbContext override |
| `DataTrustHub.Features/_Shared/Behaviors/ValidationPipelineBehavior.cs` | MediatR pipeline behavior: validates request before handler runs |
| `DataTrustHub.Features/_Shared/Extensions/FeaturesRegistration.cs` | DI setup: Carter, MediatR, validators, JWT |
| `DataTrustHub.Features/_Shared/Extensions/ResultExtensions.cs` | Maps `Result<T>` to `IResult` HTTP responses |
| `DataTrustHub.Features/Authentication/_Shared/IJwtTokenGenerator.cs` | Interface: `Generate(Guid userId, string email) → string` |
| `DataTrustHub.Features/Authentication/_Shared/JwtConfig.cs` | Config POCO bound from `appsettings.json["Jwt"]` |
| `DataTrustHub.Features/Authentication/_Shared/JwtTokenGenerator.cs` | Generates HS256 JWT tokens |
| `DataTrustHub.Features/Authentication/RegisterUser/Constants.cs` | Route, tag, min/max constraints for RegisterUser |
| `DataTrustHub.Features/Authentication/RegisterUser/RegisterUserEndpoint.cs` | Carter module: `POST /auth/register` + DTOs + handler invocation |
| `DataTrustHub.Features/Authentication/RegisterUser/RegisterUserHandler.cs` | MediatR handler: validate uniqueness → hash → persist → return userId |
| `DataTrustHub.Features/Authentication/RegisterUser/RegisterUserValidator.cs` | FluentValidation rules for `RegisterUserCommand` |
| `DataTrustHub.Features/Authentication/LoginUser/Constants.cs` | Route, tag for LoginUser |
| `DataTrustHub.Features/Authentication/LoginUser/LoginUserEndpoint.cs` | Carter module: `POST /auth/login` + DTOs + handler invocation |
| `DataTrustHub.Features/Authentication/LoginUser/LoginUserHandler.cs` | MediatR handler: find user → verify password → return JWT |
| `DataTrustHub.Features/Authentication/LoginUser/LoginUserValidator.cs` | FluentValidation rules for `LoginUserCommand` |
| `DataTrustHub.Features.Tests/Authentication/RegisterUserTests.cs` | Integration tests for RegisterUser endpoint |
| `DataTrustHub.Features.Tests/Authentication/LoginUserTests.cs` | Integration tests for LoginUser endpoint |
| `CLAUDE.md` | Auto-loaded agent context: project overview, VSA rules, code style |
| `docs/architecture/overview.md` | Architecture explanation with VSA principles and project layout |
| `docs/architecture/data-model.md` | Entity reference: User, Organization, Policy, Clearance, DataItem, Audit_Log |
| `docs/agents/workflow.md` | Step-by-step guide to launching parallel agents with git worktrees |
| `docs/agents/task-mapping.md` | GitHub issue → feature folder → branch → sprint mapping |

### Modified files
| File | Change |
|---|---|
| `DataTrustHub.sln` | Add `DataTrustHub.Features` and `DataTrustHub.Features.Tests` projects |
| `DataTrustHub.API/Program.cs` | Add `AddFeatures`, `UseAuthentication`, `MapFeatures`; add `public partial class Program {}` |
| `DataTrustHub.API/appsettings.json` | Add `Jwt` config section |
| `DataTrustHub.API/DataTrustHub.API.csproj` | Add project reference to `DataTrustHub.Features` |

---

## Task 1: Create DataTrustHub.Features project

**Files:**
- Create: `DataTrustHub.Features/DataTrustHub.Features.csproj`
- Modify: `DataTrustHub.sln`

- [ ] **Step 1: Create the project**

```bash
dotnet new classlib -n DataTrustHub.Features -f net9.0 --output DataTrustHub.Features
```

- [ ] **Step 2: Remove the default Class1.cs**

```bash
del DataTrustHub.Features\Class1.cs
```

- [ ] **Step 3: Replace the generated .csproj with the correct content**

Replace `DataTrustHub.Features/DataTrustHub.Features.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Carter" Version="8.2.1" />
    <PackageReference Include="FluentValidation" Version="12.0.0" />
    <PackageReference Include="MediatR" Version="13.0.0" />
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="9.0.10" />
    <PackageReference Include="Microsoft.IdentityModel.Tokens" Version="8.12.0" />
    <PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="8.12.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\DataTrustHub.Infrastructure\DataTrustHub.Infrastructure.csproj" />
    <ProjectReference Include="..\DataTrustHub.SharedKernel\DataTrustHub.SharedKernel.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Add the project to the solution**

```bash
dotnet sln DataTrustHub.sln add DataTrustHub.Features/DataTrustHub.Features.csproj
```

- [ ] **Step 5: Verify the project builds**

```bash
dotnet build DataTrustHub.Features/DataTrustHub.Features.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add DataTrustHub.Features/DataTrustHub.Features.csproj DataTrustHub.sln
git commit -m "feat: add DataTrustHub.Features project scaffold"
```

---

## Task 2: Create DataTrustHub.Features.Tests project

**Files:**
- Create: `DataTrustHub.Features.Tests/DataTrustHub.Features.Tests.csproj`
- Modify: `DataTrustHub.sln`, `DataTrustHub.API/Program.cs`

- [ ] **Step 1: Create the test project**

```bash
dotnet new xunit -n DataTrustHub.Features.Tests -f net9.0 --output DataTrustHub.Features.Tests
```

- [ ] **Step 2: Remove the default UnitTest1.cs**

```bash
del DataTrustHub.Features.Tests\UnitTest1.cs
```

- [ ] **Step 3: Replace the generated .csproj**

Replace `DataTrustHub.Features.Tests/DataTrustHub.Features.Tests.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="9.0.10" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="9.0.10" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\DataTrustHub.API\DataTrustHub.API.csproj" />
    <ProjectReference Include="..\DataTrustHub.Features\DataTrustHub.Features.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Add `public partial class Program {}` to the bottom of `DataTrustHub.API/Program.cs`**

Append to `DataTrustHub.API/Program.cs` after `app.Run();`:

```csharp
// Required for WebApplicationFactory in integration tests
public partial class Program { }
```

- [ ] **Step 5: Add the test project to the solution**

```bash
dotnet sln DataTrustHub.sln add DataTrustHub.Features.Tests/DataTrustHub.Features.Tests.csproj
```

- [ ] **Step 6: Create `DataTrustHub.Features.Tests/Helpers/ApiFactory.cs`**

```csharp
using DataTrustHub.Infrastructure.Persistance;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DataTrustHub.Features.Tests.Helpers;

public class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var dbDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<DContext>));
            if (dbDescriptor is not null)
                services.Remove(dbDescriptor);

            var dbName = Guid.NewGuid().ToString();
            services.AddDbContext<DContext>(options =>
                options.UseInMemoryDatabase(dbName));
        });
    }
}
```

- [ ] **Step 7: Verify the solution builds**

```bash
dotnet build DataTrustHub.sln
```

Expected: `Build succeeded.`

- [ ] **Step 8: Commit**

```bash
git add DataTrustHub.Features.Tests/ DataTrustHub.sln DataTrustHub.API/Program.cs
git commit -m "feat: add Features.Tests project and ApiFactory helper"
```

---

## Task 3: Create _Shared infrastructure inside DataTrustHub.Features

**Files:**
- Create: `DataTrustHub.Features/_Shared/Behaviors/ValidationPipelineBehavior.cs`
- Create: `DataTrustHub.Features/_Shared/Extensions/ResultExtensions.cs`
- Create: `DataTrustHub.Features/_Shared/Extensions/FeaturesRegistration.cs`

- [ ] **Step 1: Create `ValidationPipelineBehavior.cs`**

```csharp
using DataTrustHub.SharedKernel;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using System.Reflection;

namespace DataTrustHub.Features._Shared.Behaviors;

internal sealed class ValidationPipelineBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : class
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var failures = await ValidateAsync(request, cancellationToken);
        if (failures.Length == 0)
            return await next(cancellationToken);

        return BuildFailureResponse(failures);
    }

    private TResponse BuildFailureResponse(ValidationFailure[] failures)
    {
        if (IsGenericResult(typeof(TResponse)))
            return BuildGenericResultFailure(failures);

        if (typeof(TResponse) == typeof(Result))
            return (TResponse)(object)Result.Failure(CreateValidationError(failures));

        throw new ValidationException(failures);
    }

    private static bool IsGenericResult(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>);

    private static TResponse BuildGenericResultFailure(ValidationFailure[] failures)
    {
        var resultType = typeof(TResponse).GetGenericArguments()[0];
        var failureMethod = typeof(Result<>)
            .MakeGenericType(resultType)
            .GetMethod(nameof(Result<object>.ValidationFailure));

        if (failureMethod is not null)
            return (TResponse)failureMethod.Invoke(null, [CreateValidationError(failures)])!;

        throw new InvalidOperationException("Could not invoke ValidationFailure method.");
    }

    private static ValidationError CreateValidationError(ValidationFailure[] failures) =>
        new(failures.Select(f => Error.Problem(f.ErrorCode, f.ErrorMessage)).ToArray());

    private async Task<ValidationFailure[]> ValidateAsync(
        TRequest request, CancellationToken cancellationToken)
    {
        if (!validators.Any()) return [];

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(
            validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        return results
            .Where(r => !r.IsValid)
            .SelectMany(r => r.Errors)
            .ToArray();
    }
}
```

- [ ] **Step 2: Create `ResultExtensions.cs`**

```csharp
using DataTrustHub.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace DataTrustHub.Features._Shared.Extensions;

internal static class ResultExtensions
{
    internal static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult> onSuccess)
    {
        if (result.IsSuccess) return onSuccess(result.Value);

        return result.Error.Type switch
        {
            ErrorType.NotFound => Results.NotFound(result.Error),
            ErrorType.Conflict => Results.Conflict(result.Error),
            ErrorType.Validation => Results.BadRequest(result.Error),
            _ => Results.Problem(result.Error.Description)
        };
    }
}
```

- [ ] **Step 3: Create `FeaturesRegistration.cs`**

```csharp
using Carter;
using DataTrustHub.Features._Shared.Behaviors;
using DataTrustHub.Features.Authentication._Shared;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace DataTrustHub.Features._Shared.Extensions;

public static class FeaturesRegistration
{
    public static IServiceCollection AddFeatures(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddCarter();
        RegisterMediatR(services);
        RegisterJwt(services, configuration);
        return services;
    }

    public static IEndpointRouteBuilder MapFeatures(this IEndpointRouteBuilder app)
    {
        app.MapCarter();
        return app;
    }

    private static void RegisterMediatR(IServiceCollection services)
    {
        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssembly(typeof(FeaturesRegistration).Assembly);
            config.AddOpenBehavior(typeof(ValidationPipelineBehavior<,>));
        });

        services.AddValidatorsFromAssembly(
            typeof(FeaturesRegistration).Assembly,
            includeInternalTypes: true);
    }

    private static void RegisterJwt(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtConfig>(configuration.GetSection(JwtConfig.SectionName));
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();

        var jwtConfig = configuration.GetSection(JwtConfig.SectionName).Get<JwtConfig>()
            ?? throw new InvalidOperationException("Jwt configuration section is missing.");

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = BuildTokenValidationParameters(jwtConfig);
            });
    }

    private static TokenValidationParameters BuildTokenValidationParameters(JwtConfig config) =>
        new()
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = config.Issuer,
            ValidAudience = config.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(config.Secret))
        };
}
```

- [ ] **Step 4: Verify the project builds**

```bash
dotnet build DataTrustHub.Features/DataTrustHub.Features.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add DataTrustHub.Features/_Shared/
git commit -m "feat: add _Shared behaviors, result extensions, and features registration"
```

---

## Task 4: Wire DataTrustHub.Features into the API

**Files:**
- Modify: `DataTrustHub.API/DataTrustHub.API.csproj`
- Modify: `DataTrustHub.API/Program.cs`
- Modify: `DataTrustHub.API/appsettings.json`

- [ ] **Step 1: Add project reference in `DataTrustHub.API.csproj`**

Add inside the existing `<ItemGroup>` that contains `<ProjectReference>` entries:

```xml
<ProjectReference Include="..\DataTrustHub.Features\DataTrustHub.Features.csproj" />
```

- [ ] **Step 2: Update `DataTrustHub.API/appsettings.json` to add the Jwt section**

Add after the `ConnectionStrings` block:

```json
"Jwt": {
  "Secret": "DataTrustHub-dev-secret-key-minimum-32-characters-required",
  "Issuer": "DataTrustHub",
  "Audience": "DataTrustHub",
  "ExpiryMinutes": 60
}
```

- [ ] **Step 3: Replace `DataTrustHub.API/Program.cs` with the updated version**

```csharp
using DataTrustHub.Application;
using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddFeatures(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapFeatures();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
```

- [ ] **Step 4: Build the full solution**

```bash
dotnet build DataTrustHub.sln
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add DataTrustHub.API/DataTrustHub.API.csproj DataTrustHub.API/Program.cs DataTrustHub.API/appsettings.json
git commit -m "feat: wire DataTrustHub.Features into API with Carter, JWT, and authentication"
```

---

## Task 5: Implement Authentication/RegisterUser slice (TDD)

**Files:**
- Create: `DataTrustHub.Features.Tests/Authentication/RegisterUserTests.cs`
- Create: `DataTrustHub.Features/Authentication/RegisterUser/Constants.cs`
- Create: `DataTrustHub.Features/Authentication/RegisterUser/RegisterUserEndpoint.cs`
- Create: `DataTrustHub.Features/Authentication/RegisterUser/RegisterUserHandler.cs`
- Create: `DataTrustHub.Features/Authentication/RegisterUser/RegisterUserValidator.cs`

- [ ] **Step 1: Write the failing integration tests**

Create `DataTrustHub.Features.Tests/Authentication/RegisterUserTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Helpers;

namespace DataTrustHub.Features.Tests.Authentication;

public class RegisterUserTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string RegisterEndpoint = "/auth/register";
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task RegisterUser_WithValidData_Returns200WithUserId()
    {
        var response = await _client.PostAsJsonAsync(
            RegisterEndpoint,
            new { Email = "valid@example.com", Password = "Password1!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<RegisterUserResponse>();
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.UserId);
    }

    [Fact]
    public async Task RegisterUser_WithDuplicateEmail_Returns409()
    {
        var body = new { Email = "duplicate@example.com", Password = "Password1!" };
        await _client.PostAsJsonAsync(RegisterEndpoint, body);

        var response = await _client.PostAsJsonAsync(RegisterEndpoint, body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RegisterUser_WithInvalidEmail_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            RegisterEndpoint,
            new { Email = "not-an-email", Password = "Password1!" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterUser_WithShortPassword_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            RegisterEndpoint,
            new { Email = "user@example.com", Password = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private record RegisterUserResponse(Guid UserId);
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test DataTrustHub.Features.Tests/ --filter "FullyQualifiedName~RegisterUserTests"
```

Expected: Tests fail with `404 Not Found` (endpoint not yet registered).

- [ ] **Step 3: Create `Authentication/RegisterUser/Constants.cs`**

```csharp
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Authentication.RegisterUser;

internal static class Constants
{
    internal const string EndpointRoute = "/auth/register";
    internal const string EndpointTag = "Authentication";
    internal const int MinPasswordLength = 8;
    internal const int MaxEmailLength = 256;
}

internal static class Errors
{
    internal static readonly Error EmailAlreadyInUse = Error.Conflict(
        "User.EmailAlreadyInUse",
        "A user with this email address already exists.");
}
```

- [ ] **Step 4: Create `Authentication/RegisterUser/RegisterUserEndpoint.cs`**

```csharp
using Carter;
using DataTrustHub.Features._Shared.Extensions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataTrustHub.Features.Authentication.RegisterUser;

public record RegisterUserRequest(string Email, string Password);
public record RegisterUserResponse(Guid UserId);

public class RegisterUserEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .AllowAnonymous();

    private static async Task<IResult> Handle(
        RegisterUserRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new RegisterUserCommand(request.Email, request.Password);
        var result = await sender.Send(command, cancellationToken);
        return result.ToHttpResult(userId => Results.Ok(new RegisterUserResponse(userId)));
    }
}
```

- [ ] **Step 5: Create `Authentication/RegisterUser/RegisterUserHandler.cs`**

```csharp
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Authentication.RegisterUser;

public record RegisterUserCommand(string Email, string Password) : IRequest<Result<Guid>>;

public class RegisterUserHandler(DContext db, DataTrustHub.Domain.User.IPasswordHasher hasher)
    : IRequestHandler<RegisterUserCommand, Result<Guid>>
{
    private readonly DContext _db = db;
    private readonly DataTrustHub.Domain.User.IPasswordHasher _hasher = hasher;

    public async Task<Result<Guid>> Handle(
        RegisterUserCommand command,
        CancellationToken cancellationToken)
    {
        var emailExists = await CheckEmailExists(command.Email, cancellationToken);
        if (emailExists) return Result.Failure<Guid>(Errors.EmailAlreadyInUse);

        var userId = Guid.NewGuid();
        await PersistUser(userId, command.Email, command.Password, cancellationToken);
        return Result.Success(userId);
    }

    private Task<bool> CheckEmailExists(string email, CancellationToken ct) =>
        _db.Users.AnyAsync(u => u.Email == email, ct);

    private async Task PersistUser(
        Guid userId, string email, string password, CancellationToken ct)
    {
        var dbUser = new DbUser
        {
            Id = userId,
            Email = email,
            HashedPassword = _hasher.HashPassword(password)
        };
        await _db.Users.AddAsync(dbUser, ct);
        await _db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 6: Create `Authentication/RegisterUser/RegisterUserValidator.cs`**

```csharp
using FluentValidation;

namespace DataTrustHub.Features.Authentication.RegisterUser;

internal class RegisterUserValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(Constants.MaxEmailLength);

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(Constants.MinPasswordLength);
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

```bash
dotnet test DataTrustHub.Features.Tests/ --filter "FullyQualifiedName~RegisterUserTests"
```

Expected: `4 passed, 0 failed`

- [ ] **Step 8: Commit**

```bash
git add DataTrustHub.Features/Authentication/RegisterUser/ DataTrustHub.Features.Tests/Authentication/RegisterUserTests.cs
git commit -m "feat: implement Authentication/RegisterUser slice with TDD"
```

---

## Task 6: Implement Authentication/LoginUser slice (TDD)

**Files:**
- Create: `DataTrustHub.Features.Tests/Authentication/LoginUserTests.cs`
- Create: `DataTrustHub.Features/Authentication/_Shared/IJwtTokenGenerator.cs`
- Create: `DataTrustHub.Features/Authentication/_Shared/JwtConfig.cs`
- Create: `DataTrustHub.Features/Authentication/_Shared/JwtTokenGenerator.cs`
- Create: `DataTrustHub.Features/Authentication/LoginUser/Constants.cs`
- Create: `DataTrustHub.Features/Authentication/LoginUser/LoginUserEndpoint.cs`
- Create: `DataTrustHub.Features/Authentication/LoginUser/LoginUserHandler.cs`
- Create: `DataTrustHub.Features/Authentication/LoginUser/LoginUserValidator.cs`

- [ ] **Step 1: Write the failing integration tests**

Create `DataTrustHub.Features.Tests/Authentication/LoginUserTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using DataTrustHub.Features.Tests.Helpers;

namespace DataTrustHub.Features.Tests.Authentication;

public class LoginUserTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string RegisterEndpoint = "/auth/register";
    private const string LoginEndpoint = "/auth/login";
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task LoginUser_WithValidCredentials_Returns200WithToken()
    {
        await _client.PostAsJsonAsync(RegisterEndpoint,
            new { Email = "login@example.com", Password = "Password1!" });

        var response = await _client.PostAsJsonAsync(LoginEndpoint,
            new { Email = "login@example.com", Password = "Password1!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<LoginUserResponse>();
        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result.Token));
    }

    [Fact]
    public async Task LoginUser_WithWrongPassword_Returns401()
    {
        await _client.PostAsJsonAsync(RegisterEndpoint,
            new { Email = "wrongpass@example.com", Password = "Password1!" });

        var response = await _client.PostAsJsonAsync(LoginEndpoint,
            new { Email = "wrongpass@example.com", Password = "WrongPassword!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LoginUser_WithNonExistentEmail_Returns401()
    {
        var response = await _client.PostAsJsonAsync(LoginEndpoint,
            new { Email = "ghost@example.com", Password = "Password1!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LoginUser_WithEmptyEmail_Returns400()
    {
        var response = await _client.PostAsJsonAsync(LoginEndpoint,
            new { Email = "", Password = "Password1!" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private record LoginUserResponse(string Token);
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test DataTrustHub.Features.Tests/ --filter "FullyQualifiedName~LoginUserTests"
```

Expected: Tests fail with `404 Not Found`.

- [ ] **Step 3: Create `Authentication/_Shared/IJwtTokenGenerator.cs`**

```csharp
namespace DataTrustHub.Features.Authentication._Shared;

public interface IJwtTokenGenerator
{
    string Generate(Guid userId, string email);
}
```

- [ ] **Step 4: Create `Authentication/_Shared/JwtConfig.cs`**

```csharp
namespace DataTrustHub.Features.Authentication._Shared;

public class JwtConfig
{
    public const string SectionName = "Jwt";
    public required string Secret { get; init; }
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public int ExpiryMinutes { get; init; }
}
```

- [ ] **Step 5: Create `Authentication/_Shared/JwtTokenGenerator.cs`**

```csharp
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace DataTrustHub.Features.Authentication._Shared;

public class JwtTokenGenerator(IOptions<JwtConfig> jwtOptions) : IJwtTokenGenerator
{
    private readonly JwtConfig _config = jwtOptions.Value;

    public string Generate(Guid userId, string email)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = BuildClaims(userId, email);
        var token = BuildToken(claims, credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static Claim[] BuildClaims(Guid userId, string email) =>
    [
        new(JwtRegisteredClaimNames.Sub, userId.ToString()),
        new(JwtRegisteredClaimNames.Email, email),
        new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
    ];

    private JwtSecurityToken BuildToken(Claim[] claims, SigningCredentials credentials) =>
        new(
            issuer: _config.Issuer,
            audience: _config.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_config.ExpiryMinutes),
            signingCredentials: credentials);
}
```

- [ ] **Step 6: Create `Authentication/LoginUser/Constants.cs`**

```csharp
using DataTrustHub.SharedKernel;

namespace DataTrustHub.Features.Authentication.LoginUser;

internal static class Constants
{
    internal const string EndpointRoute = "/auth/login";
    internal const string EndpointTag = "Authentication";
}

internal static class Errors
{
    internal static readonly Error InvalidCredentials = Error.Problem(
        "User.InvalidCredentials",
        "Invalid email or password.");
}
```

- [ ] **Step 7: Create `Authentication/LoginUser/LoginUserEndpoint.cs`**

```csharp
using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataTrustHub.Features.Authentication.LoginUser;

public record LoginUserRequest(string Email, string Password);
public record LoginUserResponse(string Token);

public class LoginUserEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost(Constants.EndpointRoute, Handle)
           .WithTags(Constants.EndpointTag)
           .AllowAnonymous();

    private static async Task<IResult> Handle(
        LoginUserRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new LoginUserCommand(request.Email, request.Password);
        var result = await sender.Send(command, cancellationToken);

        if (result.IsSuccess) return Results.Ok(new LoginUserResponse(result.Value));

        // Always return 401 on auth failure — do not reveal whether email or password is wrong
        return Results.Unauthorized();
    }
}
```

- [ ] **Step 8: Create `Authentication/LoginUser/LoginUserHandler.cs`**

```csharp
using DataTrustHub.Features.Authentication._Shared;
using DataTrustHub.Infrastructure.Persistance;
using DataTrustHub.Infrastructure.Persistance.Model;
using DataTrustHub.SharedKernel;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DataTrustHub.Features.Authentication.LoginUser;

public record LoginUserCommand(string Email, string Password) : IRequest<Result<string>>;

public class LoginUserHandler(
    DContext db,
    DataTrustHub.Domain.User.IPasswordHasher hasher,
    IJwtTokenGenerator jwtGenerator)
    : IRequestHandler<LoginUserCommand, Result<string>>
{
    private readonly DContext _db = db;
    private readonly DataTrustHub.Domain.User.IPasswordHasher _hasher = hasher;
    private readonly IJwtTokenGenerator _jwtGenerator = jwtGenerator;

    public async Task<Result<string>> Handle(
        LoginUserCommand command,
        CancellationToken cancellationToken)
    {
        var dbUser = await FindUserByEmail(command.Email, cancellationToken);
        if (dbUser is null) return Result.Failure<string>(Errors.InvalidCredentials);

        if (!_hasher.VerifyHashedPassword(dbUser.HashedPassword, command.Password))
            return Result.Failure<string>(Errors.InvalidCredentials);

        var token = _jwtGenerator.Generate(dbUser.Id, dbUser.Email);
        return Result.Success(token);
    }

    private Task<DbUser?> FindUserByEmail(string email, CancellationToken ct) =>
        _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
}
```

- [ ] **Step 9: Create `Authentication/LoginUser/LoginUserValidator.cs`**

```csharp
using FluentValidation;

namespace DataTrustHub.Features.Authentication.LoginUser;

internal class LoginUserValidator : AbstractValidator<LoginUserCommand>
{
    public LoginUserValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}
```

- [ ] **Step 10: Run all tests to verify they pass**

```bash
dotnet test DataTrustHub.Features.Tests/
```

Expected: `8 passed, 0 failed`

- [ ] **Step 11: Commit**

```bash
git add DataTrustHub.Features/Authentication/ DataTrustHub.Features.Tests/Authentication/LoginUserTests.cs
git commit -m "feat: implement Authentication/LoginUser slice with JWT token generation"
```

---

## Task 7: Write CLAUDE.md

**Files:**
- Create: `CLAUDE.md`

- [ ] **Step 1: Create `CLAUDE.md` in the repository root**

```markdown
# DataTrustHub — Agent Context

## What this project is

DataTrustHub is a secure data management platform with policy-based access control.
Users belong to organizations. Policies define access rules. Clearances link users to policies.
DataItems are files classified by security level.

## Tech stack

- .NET 9, ASP.NET Core Minimal API
- Carter (endpoint auto-discovery via ICarterModule)
- MediatR 13 (CQRS within each slice)
- FluentValidation 12 (validated via ValidationPipelineBehavior)
- Entity Framework Core 9 with SQL Server
- DataTrustHub.SharedKernel: Result<T>, Error, ErrorType, Entity base classes

## Architecture: Vertical Slice Architecture

Code is organized by feature, not by layer. Your feature folder contains everything it needs.

```
DataTrustHub.Features/
  _Shared/               ← shared infrastructure (behaviors, extensions) — READ ONLY
  Authentication/        ← your model: study this before writing your own slice
  DataManagement/
  Sharing/
  Administration/
  MainView/
```

Each use case folder contains:
- `Constants.cs`       — route string, tag, validation thresholds, error definitions
- `*Endpoint.cs`       — ICarterModule: request/response records + HTTP handler
- `*Handler.cs`        — IRequestHandler: business logic only
- `*Validator.cs`      — AbstractValidator: input rules

## Rule #1 — Strict isolation

You work EXCLUSIVELY in `Features/<YourFeatureName>/`.
Do NOT modify files in other feature folders.
Do NOT modify `Program.cs`, `appsettings.json`, or infrastructure without explicit approval.

## Rule #2 — No cross-feature imports

Never import from another feature namespace.
Example: `using DataTrustHub.Features.Authentication` from inside `DataManagement` is FORBIDDEN.
Use `DataTrustHub.SharedKernel` types (Result, Error) to communicate outcomes.

## Rule #3 — DB migrations

If your entities need schema changes, mark the line with `// NEEDS MIGRATION`.
Do NOT run `Add-Migration` without the orchestrator designating you as "Database Owner".

## What you can import

- `DataTrustHub.SharedKernel` — Result<T>, Error, ErrorType, Entity, ValidationError
- `DataTrustHub.Infrastructure.Persistance.DContext` — inject via DI, use directly
- `DataTrustHub.Infrastructure.Persistance.Model.*` — DB entity models (DbUser, DbOrganization, etc.)
- `DataTrustHub.Domain.User.IPasswordHasher` — already registered in DI
- NuGet packages already in `DataTrustHub.Features.csproj`

## Code style rules (enforced in every PR)

- **camelCase** for local variables and parameters
- **PascalCase** for types, methods, properties, and constants
- **All comments in English**
- **No magic strings or numbers** — extract to `Constants.cs` in your slice
- **Functions max 30 lines** — extract private helper methods if needed
- **Depend on interfaces** — never `new SomeService()` inside a handler
- **One responsibility per class** — handlers handle, validators validate, endpoints route

## Running tests

```bash
dotnet test DataTrustHub.Features.Tests/
```

## Building

```bash
dotnet build DataTrustHub.sln
```

## Your task

Read the GitHub issue assigned to your worktree. Implement the feature slice in
`Features/<YourFeatureName>/`. Use `Authentication/` as the reference model.
Open a PR when all tests pass.
```

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: add CLAUDE.md with agent context, VSA rules, and code style"
```

---

## Task 8: Write technical documentation

**Files:**
- Create: `docs/architecture/overview.md`
- Create: `docs/architecture/data-model.md`
- Create: `docs/agents/workflow.md`
- Create: `docs/agents/task-mapping.md`

- [ ] **Step 1: Create `docs/architecture/overview.md`**

```markdown
# Architecture Overview

## Style: Vertical Slice Architecture

DataTrustHub uses Vertical Slice Architecture (VSA). Code is organized by feature, not by layer.
Each feature folder is a self-contained unit. There are no dependencies between feature folders.

## Project layout

```
DataTrustHub.SharedKernel     — Result<T>, Error, Entity base classes (no dependencies)
DataTrustHub.Domain           — Legacy domain entities (being phased out into slices)
DataTrustHub.Infrastructure   — EF Core DContext, DB models, PasswordHasher
DataTrustHub.Application      — Legacy CQRS handlers (being phased out into slices)
DataTrustHub.Features         — All new feature slices (VSA)
DataTrustHub.API              — ASP.NET Core host: wires everything together
DataTrustHub.Features.Tests   — Integration tests using WebApplicationFactory
DataTrustHub.WebApp           — Razor/MVC frontend (separate epic, not in scope here)
```

## Request flow

```
HTTP Request
  → Carter (route match via ICarterModule.AddRoutes)
  → Endpoint Handler (maps request to MediatR command)
  → ValidationPipelineBehavior (runs FluentValidation before handler)
  → IRequestHandler (business logic: DB read/write, token generation, etc.)
  → Result<T> returned
  → Endpoint maps Result<T> to IResult (200/400/401/404/409)
  → HTTP Response
```

## Slice anatomy

Every use case follows this exact pattern:

```
Features/<Feature>/<UseCase>/
  Constants.cs       — route, tag, validation thresholds, Error instances
  *Endpoint.cs       — ICarterModule with request/response records and HTTP handler
  *Handler.cs        — IRequestHandler with business logic
  *Validator.cs      — AbstractValidator for the command/query
```

## Shared infrastructure (_Shared)

```
Features/_Shared/
  Behaviors/ValidationPipelineBehavior.cs  — runs validators before every handler
  Extensions/FeaturesRegistration.cs       — registers Carter, MediatR, validators, JWT
  Extensions/ResultExtensions.cs           — maps Result<T> to IResult
```

## Adding a new feature

1. Create `Features/<YourFeature>/<UseCase>/` folder
2. Create `Constants.cs`, `*Endpoint.cs`, `*Handler.cs`, `*Validator.cs`
3. Carter and MediatR auto-discover your endpoint and handler — no manual registration needed
4. Write integration tests in `Features.Tests/<YourFeature>/`
```

- [ ] **Step 2: Create `docs/architecture/data-model.md`**

```markdown
# Data Model

## Entities

### User
Represents an individual account.

| Field | Type | Notes |
|---|---|---|
| Id | Guid | Primary key |
| Email | string | Unique, max 256 chars |
| HashedPassword | string | SHA-256 hashed |
| OrgId | Guid? | Foreign key → Organization |

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
| OrgId | Guid | Foreign key → Organization |

### Clearance
Links a user to a policy (grants access).

| Field | Type | Notes |
|---|---|---|
| Id | Guid | Primary key |
| UserId | Guid | Foreign key → User |
| PolicyId | Guid | Foreign key → Policy |

### DataItem
A file or data object with a security classification.

| Field | Type | Notes |
|---|---|---|
| Id | Guid | Primary key |
| Name | string | Display name |
| FileSize | long | Size in bytes |
| Content | byte[] | File content |
| OwnerUserId | Guid | Foreign key → User |
| ClassificationLevel | enum | Security level |

### Audit_Log
Tracks all significant system actions.

| Field | Type | Notes |
|---|---|---|
| Id | Guid | Primary key |
| UserId | Guid | Foreign key → User |
| EventType | string | Category of event |
| EventDetails | string | Free-text details |

## Key relationships

- User belongs to Organization (via `OrgId`)
- Clearance connects User to Policy (many-to-many bridge)
- Policy is scoped to Organization
- DataItem is owned by User
- Audit_Log records every action performed by a User

## DB access in slices

Inject `DContext` directly — it is registered in DI by Infrastructure:

```csharp
public class MyHandler(DContext db) : IRequestHandler<MyCommand, Result<Guid>>
{
    private readonly DContext _db = db;
}
```
```

- [ ] **Step 3: Create `docs/agents/workflow.md`**

```markdown
# Multi-Agent Workflow

## Principle

One Claude Code agent = One GitHub issue = One git worktree = One feature folder.

Agents work independently. They share the same repository but each agent works on
a separate branch in a separate worktree. They do not communicate.

## Prerequisites

- Git 2.5+ (for worktrees)
- .NET 9 SDK
- SQL Server instance (for running the app; tests use InMemory)
- Read `CLAUDE.md` — it is the agent's primary context

## Launching an agent

### Step 1: Create a worktree for the task

```bash
# Replace <N> and <feature-name> with the GitHub issue number and name
git worktree add worktrees/agent-<feature-name> -b feat/<N>-<feature-name>
```

Example for issue #2 (Add data):
```bash
git worktree add worktrees/agent-data -b feat/2-add-data
```

### Step 2: Open Claude Code in that worktree

Open a new terminal and navigate to the worktree:
```bash
cd worktrees/agent-<feature-name>
claude
```

Claude Code automatically reads `CLAUDE.md` from the root. The agent now has full context.

### Step 3: Give the agent its task

Paste the GitHub issue title and description into the Claude Code session.
The agent reads CLAUDE.md, identifies its feature folder, and starts implementing.

### Step 4: Agent works in isolation

The agent creates files only inside `Features/<FeatureName>/` and
`Features.Tests/<FeatureName>/`. It does not touch other folders.

### Step 5: Review and merge

When the agent opens a PR:
1. Review the diff — check that isolation rules were followed
2. Run tests: `dotnet test DataTrustHub.Features.Tests/`
3. Merge to main

### Step 6: Clean up the worktree

```bash
git worktree remove worktrees/agent-<feature-name>
```

## Database migrations

When agents need schema changes:
1. Agent writes `// NEEDS MIGRATION` next to the entity change
2. Orchestrator designates one agent as "Database Owner" for the sprint
3. Database Owner runs: `dotnet ef migrations add <MigrationName> --project DataTrustHub.Infrastructure`
4. Database Owner commits the migration files

## Running multiple agents in parallel

Sprint 1 — launch two agents simultaneously:
```bash
git worktree add worktrees/agent-auth -b feat/1-authentication
git worktree add worktrees/agent-mainview -b feat/16-main-view
# Open two Claude Code terminals, one per worktree
```

Sprint 2 — after Sprint 1 merges:
```bash
git worktree add worktrees/agent-add-data -b feat/2-add-data
git worktree add worktrees/agent-view-data -b feat/5-view-data
git worktree add worktrees/agent-admin -b feat/4-administration
```
```

- [ ] **Step 4: Create `docs/agents/task-mapping.md`**

```markdown
# Task Mapping

Maps each GitHub issue to the feature folder and branch the responsible agent works in.

| GitHub Issue | Title | Feature Folder | Branch | Sprint |
|---|---|---|---|---|
| #1 | Autentification | `Features/Authentication/` | `feat/1-authentication` | 1 |
| #16 | Build basic structure for main view | `Features/MainView/` | `feat/16-main-view` | 1 |
| #2 | Add data | `Features/DataManagement/` | `feat/2-add-data` | 2 |
| #5 | View data | `Features/DataManagement/` | `feat/5-view-data` | 2 |
| #4 | Administration of data | `Features/Administration/` | `feat/4-administration` | 2 |
| #3 | Share data | `Features/Sharing/` | `feat/3-share-data` | 3 |

## Sprint ordering rationale

- **Sprint 1** — Authentication and main view structure have no dependencies. Run in parallel.
- **Sprint 2** — Add data, view data, and administration are independent of each other. Run in parallel. Depend only on users/organizations existing (from Sprint 1 Authentication).
- **Sprint 3** — Share data depends on Add data (#2) and Administration (#4) being complete (policies and clearances must exist before sharing can be implemented).

## Use cases per feature folder

### Features/Authentication/
- `RegisterUser/` — POST /auth/register
- `LoginUser/` — POST /auth/login

### Features/DataManagement/
- `AddDataItem/` — POST /data
- `ViewDataItems/` — GET /data, GET /data/{id}

### Features/Sharing/
- `ShareData/` — POST /data/{id}/share

### Features/Administration/
- `ManageOrganizations/` — CRUD /organizations
- `ManagePolicies/` — CRUD /policies
- `ManageClearances/` — CRUD /clearances

### Features/MainView/
- `GetDashboard/` — GET /dashboard
```

- [ ] **Step 5: Commit all documentation**

```bash
git add docs/architecture/ docs/agents/ CLAUDE.md
git commit -m "docs: add architecture overview, data model, and agent workflow guides"
```

---

## Self-Review Notes

**Spec coverage check:**
- VSA project structure: Tasks 1-4 ✓
- Authentication slice as model: Tasks 5-6 ✓
- ValidationPipelineBehavior activated: Task 3 ✓
- Carter NuGet added: Task 1 ✓
- CLAUDE.md: Task 7 ✓
- Technical docs: Task 8 ✓
- Code style rules (camelCase, English, constants, 30-line functions, SOLID): Enforced in all code examples ✓
- WebApp left untouched: Not mentioned in any task ✓
- DB migration coordination: Documented in workflow.md ✓

**Type consistency check:**
- `DContext` used consistently throughout (not `AppDbContext`)
- `IPasswordHasher` fully qualified as `DataTrustHub.Domain.User.IPasswordHasher`
- `Result<T>` used with `Result.Success(value)` / `Result.Failure<T>(error)` consistently
- `ValidationPipelineBehavior` registered in `FeaturesRegistration.cs` via `AddOpenBehavior`
- `IJwtTokenGenerator` registered in `FeaturesRegistration.RegisterJwt`
- `JwtConfig.SectionName` = `"Jwt"` matches `appsettings.json` key
