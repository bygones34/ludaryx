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

## Backend integration tests

The complete backend test suite requires the local PostgreSQL service to be
healthy and the API user-secret configured as above:

```powershell
docker compose up -d --wait
dotnet test backend/Ludaryx.sln
```

The connectivity test creates the API host in Development, resolves the
registered DbContext, and calls `Database.CanConnectAsync()`. It does not
create tables, apply migrations, or change data. M0 contains no migrations;
the first meaningful migration will be introduced with Identity in M1.
