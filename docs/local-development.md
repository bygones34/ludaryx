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

For browser development, the API and Vite use the trusted ASP.NET Core
development certificate. From the repository root, export the certificate and
private key into the Git-ignored frontend certificate directory:

```powershell
dotnet dev-certs https --trust
New-Item -ItemType Directory -Force frontend/.certs | Out-Null
dotnet dev-certs https --export-path frontend/.certs/localhost.pem --format Pem --no-password
```

The export creates `localhost.pem` and `localhost.key`. Keep both local; never
commit or share the private key. Repeat the export after certificate renewal and
restart Vite. These files are required for local development/preview, but not for
the production build or frontend tests. See the
[dotnet dev-certs documentation](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-dev-certs).

Run the API with its `https` launch profile and run `npm run dev` from `frontend`.
Vite serves `https://localhost:5173` with this certificate so browser HTTPS and
the secure HMR WebSocket can both validate it. The frontend
uses `https://localhost:7034/api/v1` in development unless the public
`VITE_API_BASE_URL` is set. Never put credentials in a Vite environment
variable. With both origins using HTTPS localhost, the browser can send the
refresh cookie on credentialed auth requests. The access token stays in
JavaScript memory and is restored through refresh after a page reload.

## M2 IGDB credentials and client

Create a Confidential application in the Twitch Developer Console, with
two-factor authentication enabled on the Twitch account. IGDB uses its Client
ID and Client Secret through the server-to-server client-credentials flow;
Ludaryx users do not need to sign in with Twitch.
See the [IGDB setup instructions](https://api-docs.igdb.com/#account-creation).

Save both values in API user-secrets from the repository root without placing
the secret in shell history or a tracked file:

```powershell
$igdbClientId = Read-Host 'Twitch application Client ID'
$igdbSecret = Read-Host 'Twitch application Client Secret' -AsSecureString
$igdbCredential = [System.Management.Automation.PSCredential]::new('igdb', $igdbSecret)
@{
  'Igdb:ClientId' = $igdbClientId
  'Igdb:ClientSecret' = $igdbCredential.GetNetworkCredential().Password
} | ConvertTo-Json -Compress | dotnet user-secrets set --project backend/src/Ludaryx.Api
Remove-Variable igdbClientId, igdbSecret, igdbCredential
```

Production uses `Igdb__ClientId` and `Igdb__ClientSecret` environment variables.
Restart the API after saving or changing local user-secrets.
Do not put either value in frontend configuration. The IGDB client validates
these settings when first resolved and acquires a token on its first request;
registering it does not call Twitch or IGDB. Auth and health checks continue
to work before IGDB credentials are configured.

Infrastructure registers a typed `IIgdbClient`, a shared in-memory token
provider, and a per-process outbound limiter. Twitch credentials are sent in
the form body, never the URL. Tokens are renewed 60 seconds before expiry;
concurrent callers share acquisition. A rejected token is invalidated and the
IGDB request retried once; a late rejection cannot invalidate a newer token.
Other upstream errors are not automatically retried.

HTTP calls time out after ten seconds and respect caller cancellation. IGDB
requests start at least 250 ms apart, with at most eight in flight. HTTP client
logging is disabled for these clients; explicit logs contain only safe failure
categories. Provider response bodies, credentials, tokens, and search text
are not logged. Controlled-handler tests cover the client without real
credentials or live provider requests. Live search/details acceptance remains
a separate M2 check after the endpoints and frontend are implemented.

## M4 backlog persistence

Apply the current migrations before running the updated API. From the repository
root:

```powershell
Push-Location backend
dotnet tool restore
dotnet ef database update --project src/Ludaryx.Infrastructure --startup-project src/Ludaryx.Api -- --environment Development
Pop-Location
```

`EnforceBacklogOrderUniqueness` adds a deferrable, initially immediate UNIQUE
constraint on `(UserId, BacklogOrder)`. Non-Backlog null orders remain allowed.
Reorder defers this constraint inside its transaction and uses the same user-row
lock as library additions, updates, and deletion. Swaps are checked at commit.

The EF model describes the constraint's backing unique index. The migration uses
explicit PostgreSQL SQL for deferrability; its Down method drops the owning
constraint, not the backing index. Future migrations changing this constraint
must preserve that distinction. Reorder uses direct updates after domain
validation to avoid EF's unique-index dependency ordering during swaps.

`GET /api/v1/backlog` returns stored, owned Backlog records in persistent order.
`PUT /api/v1/backlog/order` accepts the complete list with positions `1..N`.
Neither endpoint calls IGDB. See the specification for validation and concurrency
responses. The protected `/backlog` page uses these endpoints for persistent
ordering. Drag a handle with a pointer, or use Space/Enter, arrow keys, and Escape
for keyboard dragging. Move up/Move down buttons provide another keyboard option.
Priority changes use the existing library PATCH endpoint; they do not change status.
Writes save automatically and temporarily disable further changes. Failed writes
restore the displayed saved data; a conflicting backlog reloads before more writes.

For local end-to-end verification, sign in and add games with Backlog status.
Check reorder and priority persistence after refreshing, then change a game's
status from its library details and check that it leaves the backlog. Full M4
acceptance, including the specification's ten-game scenario, remains a separate
verification step.

## Backend integration tests

The complete backend test suite requires healthy local PostgreSQL, the API
user-secret configured as above, and the current migrations applied:

```powershell
dotnet test backend/Ludaryx.sln
```

Library/backlog fixtures create uniquely named temporary PostgreSQL databases,
apply all migrations, and remove their own databases after testing. They require
the configured local database role to have database creation permission. Backlog
tests verify order swaps, ownership, invalid-request atomicity, rollback after a
mid-update failure, serialized concurrent reorders, and migration down/up without
changing the development database's library data.

The connectivity test creates the API host in Development, resolves the
registered DbContext, and calls `Database.CanConnectAsync()`. It does not
create tables, apply migrations, or change data. M0 contains no migrations.

## API health check

With PostgreSQL running and the API user-secret configured, start the API and
request `GET /health`. The endpoint checks database connectivity through the
registered DbContext. It returns HTTP 200 when PostgreSQL is reachable and
HTTP 503 when it is not. It is an operational endpoint outside `/api/v1` and
does not create schema or expose connection details.
