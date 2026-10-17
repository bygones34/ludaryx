# Ludaryx

Ludaryx is a personal game-library and backlog-management web application.
**Your games. Your journey.**

The project is being built milestone by milestone. **M0–M6 are complete and
accepted; M7 (UI / UX & Responsive) has not started.**

## Implemented features

- Registration, login/logout, protected routes, session restoration, and rotating
  refresh tokens in an HttpOnly cookie.
- Authenticated IGDB game search and details, with provider credentials kept on
  the backend.
- Private game libraries with shared game metadata, search, filters, sorting,
  pagination, and Backlog/Playing/Completed/OnHold/Dropped status tracking.
- Backlog priorities and persistent ordering through drag-and-drop, keyboard
  interaction, and Move up/Move down controls.
- Nullable ratings from 1 to 10 and personal notes up to 1,000 characters, with
  independent saves and unsaved-draft preservation.
- Server-managed first-started and first-completed dates, preserved across later
  status changes.
- A dashboard after sign-in with total/status counts, average personal rating,
  and up to five Currently Playing and Up Next games. The summary refreshes after
  library changes and backlog reordering; each user sees only their own data.

The stack is .NET 10 / ASP.NET Core, EF Core with PostgreSQL, and React /
TypeScript / Vite with React Router, TanStack Query, Tailwind CSS, and shadcn/ui.
The backend follows pragmatic Clean Architecture with feature-based organization.

Next: M7 UI/responsive polish, followed by M8 quality and hardening and M9
production deployment.

## Prerequisites

- .NET 10 SDK
- Node.js 22.12+, 24+, or 26+ and npm
- Docker Desktop with Linux containers and Docker Compose

Local browser development uses PostgreSQL on `127.0.0.1:5432`, the API on
`https://localhost:7034`, and Vite on `https://localhost:5173`. The API HTTPS
profile also listens on `http://localhost:5292`. Ensure these ports are available.
Use HTTPS localhost for browser authentication and its Secure refresh cookie.

## Run locally

From a fresh clone of this repository, run the following commands in
PowerShell from the repository root:

```powershell
Copy-Item .env.example .env
```

For an existing checkout, retain your configured `.env` rather than overwriting it.
For a fresh setup, replace its `POSTGRES_PASSWORD` placeholder with a local
password. Follow [local development setup](docs/local-development.md) to configure
the API user-secrets:

- `ConnectionStrings:LudaryxDatabase`, matching the database credentials in `.env`.
- `Jwt:SigningKey`, a separate cryptographically generated signing key.
- `Igdb:ClientId` and `Igdb:ClientSecret`, from a confidential Twitch application,
  for live game discovery.

Keep credentials outside tracked files and frontend configuration. IGDB
credentials are not required for auth or health checks.

Trust and export the local HTTPS development certificate from the repository root:

```powershell
dotnet dev-certs https --trust
New-Item -ItemType Directory -Force frontend/.certs | Out-Null
dotnet dev-certs https --export-path frontend/.certs/localhost.pem --format Pem --no-password
```

The generated certificate and private key stay in the Git-ignored `.certs`
directory. Vite requires them for local development, but builds and frontend
tests do not.

Start PostgreSQL and apply all committed migrations before running the API:

```powershell
docker compose up -d --wait
Push-Location backend
dotnet tool restore
dotnet ef database update --project src/Ludaryx.Infrastructure --startup-project src/Ludaryx.Api -- --environment Development
Pop-Location
dotnet run --project backend/src/Ludaryx.Api --launch-profile https
```

In another terminal, install frontend dependencies from the lockfile and run
Vite:

```powershell
Set-Location frontend
npm ci
npm run dev
```

Open `https://localhost:5173`, register or sign in, and use the dashboard,
discovery, library, and backlog pages. The frontend defaults to
`https://localhost:7034/api/v1`.
A request to `https://localhost:7034/health` returns HTTP 200 when PostgreSQL is
reachable and HTTP 503 otherwise. The API does not apply migrations at startup.

To stop local development, stop the API and Vite with Ctrl+C in their terminals,
then run `docker compose stop postgres`. This preserves the database volume.

## Verify

From the repository root, with dependencies installed, local PostgreSQL running,
API user-secrets configured, and current migrations applied:

```powershell
dotnet build backend/Ludaryx.sln
dotnet test backend/Ludaryx.sln
Set-Location frontend
npm run build
npm run lint
npm test
npm audit --audit-level=high
```

Backend integration tests use real local PostgreSQL. Library/backlog fixtures
create isolated temporary databases, apply migrations, and remove those databases
after testing; the configured database role needs database creation permission.
Automated IGDB tests use controlled responses. Frontend tests do not require a
running API or live IGDB.

The accepted M6 baseline recorded 304 passing backend tests and 85 passing
frontend tests, successful builds/lint, and project-owner live browser acceptance.
See the acceptance records for evidence and verification limits.

A known Edge fetch-cancellation `AbortError` remains documented in development
logs; no functional problem was reported during M6 acceptance. See the
[investigation](docs/browser-cancellation-investigation.md).

## Documentation

- [Project specification](docs/Ludaryx_Project_Specification.md) — source of
  truth for architecture, API contract, and milestones.
- [Local development setup](docs/local-development.md) — database, user-secrets,
  HTTPS certificates, IGDB configuration, migrations, and test requirements.
- Acceptance records: [M0](docs/m0-acceptance.md), [M1](docs/m1-acceptance.md),
  [M2](docs/m2-acceptance.md), [M3](docs/m3-acceptance.md),
  [M4](docs/m4-acceptance.md), [M5](docs/m5-acceptance.md),
  [M6](docs/m6-acceptance.md).
- [M5 implementation decisions](docs/m5-foundation-decisions.md) — ratings,
  notes, lifecycle semantics, and verification details.
- [M6 implementation decisions](docs/m6-foundation-decisions.md) — dashboard
  calculations, preview policies, refresh behavior, and verification details.
