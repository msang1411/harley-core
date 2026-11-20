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
| Identity | Registration, login, JWT issuing, refresh tokens | In progress |
| Gateway (YARP) | Single entry point, CORS, rate limiting | Planned |
| Blog | Blog features | Planned |
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
│   └── Services/
│       └── Identity/
│           ├── Identity.Domain/          # Entities only, no business dependencies
│           ├── Identity.Application/     # Use cases, DTOs, abstractions (interfaces)
│           ├── Identity.Infrastructure/  # EF Core, JWT signing, store implementations
│           └── Identity.Api/             # Program.cs, endpoints, Dockerfile
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
4. **Only Identity owns users.** Other services store only a `UserId` (Guid) taken from the `sub` claim of the JWT. They validate tokens using the public key from the JWKS endpoint and never call Identity per request.
5. **Share infrastructure, not domain.** If a shared library is introduced later (`BuildingBlocks/Common`), it may contain logging, auth setup, and error handling helpers. It must never contain entities or domain logic.
6. **Use cases return `Result<T>`** (see `Identity.Application/Common/Result.cs`) instead of throwing exceptions for expected failures. Endpoints convert results with `ToProblem()`.
7. **Endpoints stay thin**: bind the request, call the application service, map the result to an HTTP response. No business logic in endpoints.
8. Do not add a message broker (RabbitMQ, etc.) until there is a concrete need for async cross-service communication.

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
- Validation: simple checks live in the application service for now. FluentValidation is planned; add it only when requested.
- Never swallow exceptions silently. Log with Serilog structured logging (`{UserId}` placeholders, not string concatenation).

## Security rules

- **Never read, print, or commit** anything under `keys/`, the `.env` file, or user-secrets. Do not paste secret values into code, comments, logs, or commit messages.
- Never log passwords, raw refresh tokens, access tokens, or password hashes.
- Do not weaken security settings (token lifetime, lockout, password rules, signature validation) to make something "just work".
- Configuration values that are secrets go through user-secrets (local) or environment variables (Docker). Non-secret defaults go in `appsettings.json`.
- Treat all data from requests as untrusted.

## Testing

- Unit tests for application logic; integration tests with **Testcontainers + real PostgreSQL** (no in-memory database, behavior differs for `ExecuteUpdateAsync` and constraints).
- Tests live under `tests/`, for example `tests/Identity.Tests`.
- Critical scenarios to cover for Identity: register (success, duplicate, weak password), login (success, wrong password, lockout), refresh rotation, refresh reuse detection (family revoked), logout, `/auth/me` with and without a token.

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

1. Identity: FluentValidation for requests.
2. Identity: integration tests with Testcontainers.
3. Identity: background cleanup of expired refresh tokens; optional absolute session lifetime.
4. Gateway with YARP: route `/auth/*`, CORS, rate limiting on login and refresh.
5. `BuildingBlocks/Common`: shared JWT validation (via JWKS), Serilog, and error-handling extensions.