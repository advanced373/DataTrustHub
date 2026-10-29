# ADR-0005: MVC WebApp as a client of the API, with the JWT in the auth cookie

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** #16, #30, #31, #32, #33, #37

## Context

`DataTrustHub.WebApp` was the default MVC template and referenced the legacy `Application` and
`Infrastructure` projects directly. The API (`DataTrustHub.Features`) authenticates with JWT bearer tokens.

## Decision

- Keep ASP.NET Core MVC (Razor views) for the frontend.
- The WebApp talks to the API only over HTTP, through typed `HttpClient`s (`IAuthApiClient`,
  `IDataApiClient`, `IAdminApiClient`). It has no reference to `Application` / `Infrastructure`
  and never touches the database.
- On sign-in, the API's JWT is stored as a claim in the WebApp's own cookie-auth ticket. The cookie is
  encrypted by Data Protection, `HttpOnly`, `SameSite=Strict`, `Secure` outside Development, and
  expires with the token. `BearerTokenHandler` forwards the token on API calls.
- The WebApp reads the token's claims (name, role) only for display. The API validates the token on every call.
- Every page requires a signed-in user (global `AuthorizeFilter`), and every POST validates antiforgery tokens.
- A `401` from the API signs the user out and redirects to sign in.

## Alternatives considered

- **SPA (React, Blazor WASM, Angular).** More setup; the token would live in the browser, exposed to scripts.
- **WebApp calling the database directly.** Duplicates business and access rules outside the API.

## Consequences

- Business rules live in one place, the API. The WebApp is a thin client.
- The WebApp needs `Api:BaseUrl`, and the API must be running (both default to the `https` launch profile).
- Server-side rendering means one round trip per action. Fine for the current scope.
