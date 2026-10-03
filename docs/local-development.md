# Local database setup

Run commands from the repository root. Docker Desktop must be running with
Linux containers enabled.

1. Copy `.env.example` to `.env` and replace the password placeholder with a
   local password. `.env` is ignored by Git; never commit its contents.
2. Start PostgreSQL with `docker compose up -d --wait`.
3. Configure the API user-secret `ConnectionStrings:LudaryxDatabase` using the
   same database name, username, and password. Read these values from `.env`
   rather than placing a secret-bearing connection string in shell history:

```powershell
$localSettings = @{}
Get-Content .env | Where-Object { $_ -match '^[A-Z_]+=' } | ForEach-Object {
  $parts = $_ -split '=', 2
  $localSettings[$parts[0]] = $parts[1]
}
$connectionBuilder = [System.Data.Common.DbConnectionStringBuilder]::new()
$connectionBuilder['Host'] = 'localhost'
$connectionBuilder['Port'] = 5432
$connectionBuilder['Database'] = $localSettings['POSTGRES_DB']
$connectionBuilder['Username'] = $localSettings['POSTGRES_USER']
$connectionBuilder['Password'] = $localSettings['POSTGRES_PASSWORD']
@{ 'ConnectionStrings:LudaryxDatabase' = $connectionBuilder.ConnectionString } |
  ConvertTo-Json -Compress |
  dotnet user-secrets set --project backend/src/Ludaryx.Api
```

The configuration contract is `ConnectionStrings:LudaryxDatabase` (accessible
through `GetConnectionString("LudaryxDatabase")`). In Development, ASP.NET Core
loads user-secrets automatically. Infrastructure registers `LudaryxDbContext`
with EF Core and Npgsql. API startup fails with a configuration error if the
connection string is missing or blank.
User-secrets are stored outside the repository and are for local development.

M1 authentication also needs a JWT signing key. Generate a separate 256-bit
key and save it to the API user-secrets store without printing it or writing it
to a tracked file:

```powershell
@{ 'Jwt:SigningKey' = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) } |
  ConvertTo-Json -Compress |
  dotnet user-secrets set --project backend/src/Ludaryx.Api
```

`Jwt:Issuer` and `Jwt:Audience` are public values in API configuration. The
signing key is secret; API startup fails clearly if it is missing or shorter
than 32 decoded bytes. Use a separate environment variable for the signing key
outside local development.

PostgreSQL uses the official `postgres:18` image. The host mapping is
`127.0.0.1:5432:5432`; API launch ports remain unchanged. The named volume
`ludaryx_postgres_data` is mounted at `/var/lib/postgresql`. The major-version
image tag receives patch updates on pull; it does not automatically upgrade
the database to a new major version.

Use `docker compose ps` to inspect readiness. The health check runs
`pg_isready` every five seconds. `docker compose down` preserves the volume;
`docker compose down -v` deletes local database data. Environment credentials
initialize a new volume only: editing `.env` does not change the password of
an existing database. Keep `.env` and the API user-secret synchronized.

## M1 authentication migrations

M1 adds the first migration for the ASP.NET Core Identity user schema and a
second migration for refresh-session records. After PostgreSQL is healthy
and the API connection string is configured, apply them
from the `backend` directory before running the M1 integration tests:

```powershell
dotnet tool restore
dotnet ef database update --project src/Ludaryx.Infrastructure --startup-project src/Ludaryx.Api --context LudaryxDbContext
```

The local `dotnet-ef` version is pinned in `backend/dotnet-tools.json`. The
migrations use the API's Development user-secret and create Identity user
tables, refresh-session records, and EF's migration-history table. The
application does not apply migrations automatically at startup. Integration
tests check that the Identity migration was applied and exercise refresh-token
rotation against the local database.

## M1 refresh cookie and browser origin

The API allows credentialed browser requests from the exact Development origin
`https://localhost:5173`. Configure another environment's exact frontend
origins through `Cors:AllowedOrigins` (for example,
`Cors__AllowedOrigins__0`); the API refuses to start if none are configured.
Refresh and logout also require a matching `Origin` header. The frontend must
send credentials on auth requests. Login and refresh set a host-only,
`HttpOnly`, `Secure`, `SameSite=Lax` cookie scoped to `/api/v1/auth`; the refresh
token is never returned in JSON. Local browser testing therefore needs both
frontend and API on HTTPS localhost.

For M1 browser development, trust the ASP.NET Core development certificate
with `dotnet dev-certs https --trust`, run the API with its `https` launch
profile, and run `npm run dev` from `frontend`. Vite serves
`https://localhost:5173` using a locally generated, untrusted development
certificate; accept its browser warning for local development. The frontend
uses `https://localhost:7034/api/v1` in development unless the public
`VITE_API_BASE_URL` is set. Never put credentials in a Vite environment
variable. With both origins using HTTPS localhost, the browser can send the
refresh cookie on credentialed auth requests. The access token stays in
JavaScript memory and is restored through refresh after a page reload.

## Backend integration tests

The complete backend test suite requires healthy local PostgreSQL, the API
user-secret configured as above, and the M1 migrations applied:

```powershell
dotnet test backend/Ludaryx.sln
```

The connectivity test creates the API host in Development, resolves the
registered DbContext, and calls `Database.CanConnectAsync()`. It does not
create tables, apply migrations, or change data. M0 contains no migrations.

## API health check

With PostgreSQL running and the API user-secret configured, start the API and
request `GET /health`. The endpoint checks database connectivity through the
registered DbContext. It returns HTTP 200 when PostgreSQL is reachable and
HTTP 503 when it is not. It is an operational endpoint outside `/api/v1` and
does not create schema or expose connection details.
