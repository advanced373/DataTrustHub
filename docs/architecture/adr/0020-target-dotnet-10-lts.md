# ADR-0020: Target .NET 10 (LTS) before the first release

- **Status:** Accepted
- **Date:** 2026-10-27
- **Related:** #140; amends [ADR-0017](0017-azure-deployment.md) and
  [ADR-0018](0018-staging-and-production-with-migrations-in-ci-cd.md)

## Context

The solution targets .NET 9 and EF Core 9. .NET 9 is a Standard Term Support release and reaches
end of support on **2026-11-10**. After that date it gets no security patches. ADR-0017 relies on
App Service's built-in .NET runtime, which Microsoft keeps patched, so the code must target a
runtime that is still in support.

The application has not been released yet (#22, #102). Nothing runs in Azure, so the move costs
no migration of a running system. .NET 10 is the current Long Term Support release, supported
until November 2028.

The user decided on 2026-10-27 to move to .NET 10 before the first deploy.

## Decision

1. **Every project targets `net10.0`**: API, WebApp, Features, Domain, Infrastructure,
   SharedKernel and the tests.
2. **Microsoft packages move to one aligned 10.0.x patch version**: ASP.NET Core, EF Core and its
   providers, `Microsoft.Extensions.*`. Mixed patch versions are not allowed.
3. **Tooling follows:** the `dotnet-ef` local tool (`.config/dotnet-tools.json`) is pinned to the
   same 10.0.x version, and CI sets up .NET 10.
4. **Azure:** both App Service web apps run the built-in Linux runtime `DOTNETCORE|10.0`. The
   migration bundle stays framework-dependent (ADR-0018 §2), now on .NET 10.
5. **No schema change.** The EF model is unchanged. The regenerated snapshot and designer files may
   change only their product-version annotation.
6. **Later LTS-to-LTS moves** are planned at least one month before the running version's end of
   support. STS releases are not adopted for production.

## Alternatives considered

- **Stay on .NET 9.** Rejected: from 2026-11-10 the deployed apps would run without security
  patches, on a platform whose purpose is protecting classified data.
- **Release on .NET 9 first, then upgrade.** Rejected: the first release would go out with less
  than two weeks of support left, and the upgrade would then also have to be applied to two
  running environments.
- **Containers with our own base image** to control the runtime. Rejected for the same reasons as
  in ADR-0017: patching the images would be ours.

## Consequences

**Easier:**
- Supported runtime until November 2028, with LTS patches from App Service.
- One major version across code, tools, CI and Azure.

**Harder:**
- Developers need the .NET 10 SDK.
- Third-party packages (Carter, FluentValidation, Scrutor, Mapperly) must support .NET 10; #140
  checks this.

**Changes to ADR-0017 and ADR-0018:**
- ADR-0017 §1: the web apps run .NET 10 on Linux, not .NET 9.
- ADR-0018 §2–3: the migration bundle is framework-dependent on .NET 10, and `dotnet-ef` is pinned
  to 10.0.x instead of 9.0.x.

**Follow-up:**
- #140: the upgrade itself, with `CLAUDE.md` and the docs saying .NET 10.
- #101: Bicep sets `DOTNETCORE|10.0` as the runtime.
