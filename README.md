# Ludaryx

Ludaryx is a personal game-library and backlog-management web application.
The project is being built milestone by milestone; the current M0 foundation
provides a .NET API, a React frontend, and local PostgreSQL. Authentication,
game discovery, and library features are not implemented yet.

## Prerequisites

- .NET 10 SDK
- Node.js 22.12+, 24+, or 26+ and npm
- Docker Desktop with Linux containers and Docker Compose

The local defaults use PostgreSQL on `127.0.0.1:5432`, the API HTTP profile on
`http://localhost:5292`, and Vite on `http://localhost:5173`. Ensure these
ports are available. The API also has an HTTPS launch profile; this setup uses
HTTP for local verification.

## Run locally

From a fresh clone of this repository, run the following commands in
PowerShell from the repository root:

```powershell
Copy-Item .env.example .env
```

Replace the `POSTGRES_PASSWORD` placeholder in the ignored `.env` file with
a local password. Configure the API's
`ConnectionStrings:LudaryxDatabase` user-secret with the same credentials by
following [local database setup](docs/local-development.md). The password and
connection string must remain outside tracked files.

Start PostgreSQL and run the API:

```powershell
docker compose up -d --wait
dotnet run --project backend/src/Ludaryx.Api --launch-profile http
```

In another terminal, install frontend dependencies from the lockfile and run
Vite:

```powershell
Set-Location frontend
npm ci
npm run dev
```

Open `http://localhost:5173` to see the foundation page. A request to
`http://localhost:5292/health` returns HTTP 200 when the API can reach
PostgreSQL. The health check returns HTTP 503 when PostgreSQL is unreachable.

## Verify

With the local database running and the API user-secret configured:

```powershell
dotnet build backend/Ludaryx.sln
dotnet test backend/Ludaryx.sln
Set-Location frontend
npm run build
npm run lint
npm test
```

The backend integration tests use the real local PostgreSQL instance. M0 has
no migration or application tables; the first migration is planned for M1
with ASP.NET Core Identity. Frontend tests do not require IGDB or the API.

## Documentation

- [Project specification](docs/Ludaryx_Project_Specification.md) — source of
  truth for architecture, API contract, and milestones.
- [Local database setup](docs/local-development.md) — Docker credentials,
  user-secrets, and health-check details.
- [M0 acceptance record](docs/m0-acceptance.md) — checks performed and their
  limits.
