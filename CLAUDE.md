# CLAUDE.md

Guidance for Claude Code when working in the `harley-core` repository.

## Language rules (important)

- **All code comments MUST be written in English.** This includes `//` comments, `/* */` blocks, XML doc comments (`///`), SQL comments, YAML/Dockerfile comments, and comments in `.csproj` / `.props` files.
- Log messages, exception messages, commit messages, and PR descriptions are also in English.
- Identifiers (classes, methods, variables, files) are in English.
- Do not leave Vietnamese text in code comments.
- Comments should explain **why**, not restate **what** the code does. Do not add noise comments.
- **Conversation language**: reply to the maintainer in the same language they write in (usually Vietnamese). This applies only to chat. Everything written into the repository (code, comments, logs, exception messages, commit messages, docs) stays in English, even when the maintainer's request is in Vietnamese.

## Project overview

`harley-core` is a personal platform built as a set of .NET microservices. Planned services:

| Service | Purpose | Status |
|---|---|---|
| Identity | Registration, login, JWT issuing, refresh tokens | Done (core flows, validation, integration tests) |
| Gateway (YARP) | Single entry point, CORS, rate limiting | Done |
| BuildingBlocks.Common | Shared JWT validation via JWKS for downstream services | Done |
| Blog | Blog features | Next |
| Finance | Money management | Planned |
| English | English learning features | Planned |

The maintainer is building this project step by step and wants to **understand** the code. Prefer clear, simple solutions over clever ones. Explain non-obvious design decisions briefly.

## Tech stack

- .NET 9 / ASP.NET Core 9 (Minimal APIs, no MVC controllers)
- EF Core 9 with `Npgsql.EntityFrameworkCore.PostgreSQL`
- ASP.NET Core Identity (`UserManager`, password hashing, lockout)
- JWT access tokens signed with **RS256**; opaque refresh tokens stored hashed in the DB
- Serilog for logging, Scalar + built-in OpenAPI for API docs (Development only)
- PostgreSQL 17 and Docker Compose for local infrastructure

## Repository layout

```
harley-core/
├── src/
│   ├── Gateway/
│   │   └── Gateway.Api/                  # YARP reverse proxy, CORS, rate limiting (port 8080)
│   ├── BuildingBlocks/
│   │   └── BuildingBlocks.Common/        # Shared infrastructure helpers (JWT validation via JWKS)
│   └── Services/
│       └── Identity/
│           ├── Identity.Domain/          # Entities only, no business dependencies
│           ├── Identity.Application/     # Use cases, DTOs, validators, abstractions
│           ├── Identity.Infrastructure/  # EF Core, JWT signing, store implementations
│           └── Identity.Api/             # Program.cs, endpoints, Dockerfile (port 5001 locally)
├── tests/
│   └── Identity.Tests/                   # Validator unit tests + integration tests (Testcontainers)
├── docker/postgres/init.sql              # Creates one database per service
├── docker-compose.yml
├── Directory.Build.props                 # Shared settings (net9.0, nullable, implicit usings)
├── Directory.Packages.props              # Central package version management
├── global.json
├── keys/                                 # RSA keys, NEVER commit, NEVER read
└── .env                                  # Secrets, NEVER commit, NEVER read
```

Future services follow the same pattern under `src/Services/<Name>/`. Very simple services may collapse into fewer projects.

## Architecture rules

1. **Dependency direction**: `Domain` <- `Application` <- `Infrastructure` and `Api`. `Domain` references nothing internal. `Application` references only `Domain`. Never reference `Infrastructure` from `Application`.
2. **Interfaces live in Application, implementations live in Infrastructure.** Example: `ITokenService` and `IRefreshTokenStore` are in Application; `JwtTokenService` and `RefreshTokenStore` are in Infrastructure.
3. **Database per service.** One PostgreSQL instance, one database per service (`identity_db`, `blog_db`, `finance_db`, `english_db`). A service never reads another service's database.
4. **Only Identity owns users.** Other services store only a `UserId` (Guid) taken from the `sub` claim of the JWT. They validate tokens with `BuildingBlocks.Common` (`AddJwtAuthentication`), which downloads Identity's public key through the OIDC discovery document and the JWKS endpoint, then caches it. They never call Identity per request.
5. **Share infrastructure, not domain.** `BuildingBlocks.Common` may contain auth setup, logging, and error handling helpers. It must never contain entities or domain logic. Identity itself does not use it (it validates tokens with its own signing key).
6. **Use cases return `Result<T>`** (see `Identity.Application/Common/Result.cs`) instead of throwing exceptions for expected failures. Endpoints convert results with `ToProblem()`.
7. **Endpoints stay thin**: bind the request, call the application service, map the result to an HTTP response. No business logic in endpoints.
8. Do not add a message broker (RabbitMQ, etc.) until there is a concrete need for async cross-service communication.
9. **Services validate JWTs themselves.** The Gateway does not validate tokens; it only routes, applies CORS, and rate limits. Never assume the Gateway is the only way to reach a service.
10. **Internal endpoints are not routed by the Gateway** (`/health`, `/.well-known/*`). Add a Gateway route only when an endpoint must be public.

## Identity service: authentication design

Endpoints:

```
POST /auth/register   -> creates user, returns access + refresh token
POST /auth/login      -> validates credentials, returns access + refresh token
POST /auth/refresh    -> rotates the refresh token, returns a new pair
POST /auth/logout     -> revokes the refresh token family (always returns 204)
GET  /auth/me         -> requires a valid access token
GET  /.well-known/jwks.json -> public signing key (JWKS)
GET  /health          -> health check including PostgreSQL
```

Design decisions that must be preserved:

- **Access token**: JWT, RS256, short-lived (`Jwt:AccessTokenMinutes`, default 15). Claims: `sub`, `email`, `jti`, optional `name`, `role`. `MapInboundClaims` is disabled so claim names stay short.
- **Refresh token**: 32 random bytes (base64url). Only its **SHA-256 hash** is stored; the raw token is never persisted or logged.
- **Rotation**: every refresh token is single-use. A refresh marks the old token as used and issues a new one in the same family.
- **Reuse detection**: presenting an already-used token revokes the **entire family**. Keep the "used" check before the "expired" check.
- **Atomic use**: `TryMarkUsedAsync` is a single `UPDATE ... WHERE UsedAtUtc IS NULL AND RevokedAtUtc IS NULL` (via `ExecuteUpdateAsync`). Do not replace it with a read-then-write sequence; that would introduce a race condition.
- **Uniform errors**: wrong email, wrong password, and locked account all return the same `InvalidCredentials` error. All refresh failures return the same `InvalidRefreshToken` error. The real reason is only logged server-side.
- **Lockout**: handled by ASP.NET Identity (`AccessFailedAsync`, `IsLockedOutAsync`).
- **JWKS**: build the JWK manually from `ExportParameters(includePrivateParameters: false)`. Never serialize the private key or use helpers that may include private parameters.

## Gateway design

- The Gateway is a YARP reverse proxy (`src/Gateway/Gateway.Api`). Routes and clusters live in `appsettings.json` under `ReverseProxy`.
- It only routes, applies CORS, and rate limits. It contains no business logic and does not validate JWTs.
- Rate limiting uses the built-in ASP.NET Core rate limiter (sliding window, partitioned by client IP). Policies: `auth-credentials` (login and register) and `auth-refresh`. Limits come from the `RateLimiting` config section. Counters live in memory, per instance.
- CORS uses one named policy, `frontend`. Allowed origins come from `Cors:AllowedOrigins` (exact origins, no trailing slash). Never use `*` together with credentials. Add `PUT` and `DELETE` to the allowed methods when Blog needs them.
- Middleware order matters: `UseCors` comes before `UseRateLimiter`, so preflight requests do not consume permits.
- Behind a reverse proxy in production, configure `ForwardedHeaders` with explicit trusted proxies. Otherwise every client appears with the proxy's IP and they all share one rate limit.
- Adding a new service means adding a cluster and its routes to the Gateway configuration.
- Public API routes live under `/api/<app>/...` (for example `/api/auth/*`, `/api/blog/*`, `/api/lazy-finance/*`, `/api/english/*`). The Gateway strips the prefix with the YARP `PathRemovePrefix` transform, so services expose unprefixed paths such as `/auth/login` and `/posts` and never know about `/api`.
- Route `Path` values, including the ones carrying rate limit policies, always use the full public path (for example `/api/auth/login`). The most specific route wins over `{**catch-all}`.
- The Gateway's own `/` and `/health` stay outside `/api` (infrastructure endpoints).
- Services must not emit absolute or prefixed URLs (for example `Location` headers), because they do not know their public path. Media URLs inside post HTML are stored as relative paths (`/api/blog/media/{id}`).
- The frontend lives in a **separate repository**. In production the frontend and `/api` are served from the same domain through a reverse proxy, so no CORS is needed there. In development the frontend dev server proxies `/api` to the Gateway (`http://localhost:8080`). The Gateway's CORS policy remains for development setups that use a different origin.

## Roles and apps (decided, implemented in Phase 1)

- Roles live in **Identity** (ASP.NET Identity role table) and are issued in the access token as `role` claims. Other services only read claims; they never assign roles.
- App keys: `blog`, `lazy-finance`, `english`. The same key is used in public paths (`/api/lazy-finance`), in the activation endpoint, and in role names.
- Roles: `blog-member`, `blog-writer`, `blog-admin`, `lazy-finance-member`, `english-member`, and `super-admin`.
- Hierarchy is expressed in **policies inside each service**, not by assigning several roles to one user. Example for Blog: `blog.comment` = member, writer, or admin; `blog.write` = writer or admin; `blog.moderate` = admin.
- `super-admin` satisfies every policy through one shared authorization handler in `BuildingBlocks.Common`. Ownership checks written in code ("only my own post") must check super-admin explicitly.
- There is no self-service path to `super-admin`. The first super-admin is seeded from configuration or secrets at startup, only when none exists.
- Activation: `POST /auth/apps/{app}/activate` (authenticated, allowed app keys only, assigns only the `*-member` role of that app). The client then calls `/auth/refresh` to get a token containing the new role. Role changes reach existing tokens after at most one access token lifetime (15 minutes).
- Blog shows `DisplayName`, never the email. App-specific data in other services is created lazily on the first request, keyed by `UserId`.

## BuildingBlocks.Common: JWT validation for downstream services

Usage in a service:

```csharp
builder.Services.AddJwtAuthentication(builder.Configuration);
// ...
app.UseAuthentication();
app.UseAuthorization();
```

- Required config section `Jwt`: `Issuer`, `Audience`, `MetadataAddress` (Identity's `/.well-known/openid-configuration`), and `RequireHttpsMetadata` (false only for local plain HTTP).
- Identity publishes a minimal OIDC discovery document that points to `/.well-known/jwks.json`. The JwtBearer handler downloads and caches the keys and refreshes them when it sees an unknown `kid` (key rotation).
- Only RS256 is accepted (`ValidAlgorithms`). Signature, issuer, audience, and lifetime are always validated.
- `ClaimsPrincipal.GetUserId()` returns the user id from the `sub` claim.
- Inside Docker, `MetadataAddress` uses the internal host name (for example `http://identity-api:8080/...`), never the Gateway.
- A service's Dockerfile must copy the `BuildingBlocks.Common` csproj before `dotnet restore`.

## Common commands

Run from the repository root.

```bash
# Build everything
dotnet build

# Start infrastructure only (recommended while developing)
docker compose up -d postgres

# Run the Identity API locally (http://localhost:5001)
dotnet run --project src/Services/Identity/Identity.Api

# Run the whole stack in Docker
docker compose up -d --build

# Add an EF Core migration
dotnet ef migrations add <MigrationName> \
  -p src/Services/Identity/Identity.Infrastructure \
  -s src/Services/Identity/Identity.Api \
  -o Persistence/Migrations

# Quick checks
curl http://localhost:5001/health
curl http://localhost:5001/.well-known/jwks.json

# Run the Gateway locally (http://localhost:8080); Identity must be running on port 5001
dotnet run --project src/Gateway/Gateway.Api

# Run all tests (Docker must be running)
dotnet test
```

Notes:

- Inside Docker the database host is `postgres`; locally it is `localhost`.
- `Database:AutoMigrate` is enabled in Development and in the Docker Compose setup. Production migrations must be run as a separate, controlled step.
- `docker/postgres/init.sql` only runs when the Postgres volume is first created. Changing it or the DB password later requires `docker compose down -v`.

## Coding conventions

- Target `net9.0`, nullable reference types enabled, implicit usings enabled (set in `Directory.Build.props`).
- **Package versions are managed centrally** in `Directory.Packages.props`. Never put a `Version` attribute on a `PackageReference` in a `.csproj`. Add packages with `dotnet add package`, then verify the version landed in `Directory.Packages.props`.
- Use file-scoped namespaces and primary constructors for dependency injection.
- Use `async`/`await` end to end and pass a `CancellationToken` where a method touches I/O and the signature allows it.
- Use `DateTime.UtcNow` only; store and compare all times in UTC.
- Prefer small, focused classes. Do not introduce MediatR, AutoMapper, or similar libraries unless the maintainer asks.
- Validation: request shape checks (required, max length, format) use FluentValidation. Validators live in `Application` and are attached to endpoints with `.WithValidation<T>()` (an endpoint filter in `Api`). Business rules (duplicate email, password complexity) stay in the application service and ASP.NET Identity. Do not duplicate the password policy in validators.
- Never swallow exceptions silently. Log with Serilog structured logging (`{UserId}` placeholders, not string concatenation).

## Security rules

- **Never read, print, or commit** anything under `keys/`, the `.env` file, or user-secrets. Do not paste secret values into code, comments, logs, or commit messages.
- Never log passwords, raw refresh tokens, access tokens, or password hashes.
- Do not weaken security settings (token lifetime, lockout, password rules, signature validation) to make something "just work".
- Configuration values that are secrets go through user-secrets (local) or environment variables (Docker). Non-secret defaults go in `appsettings.json`.
- Treat all data from requests as untrusted.

## Testing

- Tests live under `tests/`. Currently: `tests/Identity.Tests`.
- Unit tests for pure logic (for example validators). Integration tests use **Testcontainers + real PostgreSQL**; no in-memory database.
- Integration tests share one `IdentityApiFactory` (one API host, one PostgreSQL container) through the `Identity API` xUnit collection. Every test class must carry `[Collection(ApiCollection.Name)]`.
- The test host receives its configuration through **environment variables**, because `Program.cs` reads configuration eagerly. It runs in the `Testing` environment with a throwaway RSA key.
- Every test creates its own user with a unique email. Never rely on database state left by another test.
- `JwksConsumerTests` is a contract test: it starts a tiny host that uses `BuildingBlocks.Common` and validates tokens issued by Identity, so changes to the JWKS or discovery endpoints cannot silently break downstream services.
- Critical Identity scenarios covered: register, login, lockout, `/auth/me`, refresh rotation, reuse detection, concurrent refresh, logout, JWKS.
- `dotnet test` requires Docker to be running.

## Known issues

- Refresh token race: if a losing concurrent request revokes the family before the winning request inserts its new token, the new token stays valid. Fix: after inserting the new token, re-check whether the family was revoked; if so, revoke the new token and return 401.
- Expired and used refresh tokens are never deleted (a cleanup job is planned).

## Git workflow

- Keep commits small and focused; one logical change per commit.
- Commit messages in English, imperative mood, for example: `Add refresh token rotation`.
- Do not commit build output (`bin/`, `obj/`), `.env`, or `keys/`.
- If a change adds an EF migration, commit the migration files together with the model change.

## How to work in this repo

1. Before large changes, briefly state the plan and which files will be touched.
2. Make the smallest change that fully solves the task. Do not refactor unrelated code.
3. After changing code, run `dotnet build` and fix all errors and new warnings.
4. If you change an entity or `DbContext` mapping, create a migration and review the generated file. A migration should only contain the intended changes.
5. If you add or change behavior, add or update tests when a `tests/` project exists.
6. Keep this file up to date when architecture decisions change.

## Roadmap

0. Phase 0 (done): public API under `/api`, Gateway strips the prefix.
1. Phase 1: role system in Identity (`DisplayName`, role seeding, super-admin seeding, app activation endpoint, role management endpoints, super-admin handler and `AppRoles` in `BuildingBlocks.Common`).
2. Phase 2: Blog service skeleton (four projects, `blog_db`, Dockerfile, Compose, Gateway route `/api/blog`, JWT via `BuildingBlocks.Common`, test infrastructure).
3. Phase 3: categories and tags.
4. Phase 4: posts (workflow Draft, PendingReview, Published, Rejected, Unpublished; server-side HTML sanitizer; Vietnamese-aware slugs; pagination and filtering).
5. Phase 5: image upload (magic-byte validation, EXIF stripping, `IMediaStorage`).
6. Phase 6: comments (plain text, soft delete, moderation, rate limiting at the Gateway).
7. Phase 7: polish (full-text search, orphan media cleanup, edit-after-publish).
8. Later: fix the refresh token family race, cleanup of expired tokens, Gateway tests, CI, observability.