# Orka Migration Policy

Production and Staging must not run EF Core migrations from API startup.
Migrations are a controlled deployment step, reviewed and applied before the
new application version is considered ready.

## Environment Policy

- Development: `Database:AutoMigrateOnStartup=false` by default. Use
  `scripts/reset-dev-db.ps1` or `dotnet ef database update` for local DB setup.
  A developer may opt in locally with `Database:AutoMigrateOnStartup=true`.
- Staging: startup auto-migration is forbidden. Pending migrations make
  `/health/ready` unhealthy when `RequireAppliedMigrationsForReadiness=true`.
- Production: startup auto-migration is forbidden. Pending migrations make
  `/health/ready` unhealthy when `RequireAppliedMigrationsForReadiness=true`.

If `Database:AutoMigrateOnStartup=true` is configured in Staging or Production,
the API fails fast with a generic migration policy error. No connection strings
or secrets are written to the error message.

## Production/Staging Deploy Steps

1. Build and test the application.
2. Generate an idempotent migration script:

   ```powershell
   dotnet ef migrations script --idempotent --project Orka.Infrastructure --startup-project Orka.API -o artifacts/migrations/<name>.sql
   ```

3. Review the script before it touches any shared database.
4. For destructive migrations, take a DB backup or snapshot before applying.
5. Apply the script to Staging first.
6. Verify `/health/ready` is healthy and that `ef-migrations` reports all
   migrations applied.
7. Repeat the same backup, apply, and health verification flow for Production.

## Production/Staging CORS allowlist

Staging and Production must provide explicit browser origins through
`Cors:AllowedOrigins`. Empty values and `*` are rejected at startup.

Example environment variables:

```powershell
# Staging
$env:Cors__AllowedOrigins__0 = "https://staging.orka.example"

# Production
$env:Cors__AllowedOrigins__0 = "https://app.orka.example"
$env:Cors__AllowedOrigins__1 = "https://www.orka.example"
```

Do not use `Cors:AllowAnyOriginInDevelopment=true` outside local Development.
If Staging or Production starts with an empty allowlist, wildcard origin, or
development-only allow-any setting, the API fails fast before serving traffic.

## Security headers / CSP

Staging and Production enable an enforced Content Security Policy by default.
Development leaves CSP disabled by default so Vite HMR and local debugging are
not blocked.

Default enforced policy:

```text
default-src 'self';
object-src 'none';
base-uri 'self';
frame-ancestors 'none';
script-src 'self';
style-src 'self' 'unsafe-inline';
img-src 'self' data: blob: https://image.pollinations.ai;
font-src 'self' data:;
connect-src 'self' https: wss:;
media-src 'self' blob: data:;
frame-src 'none'
```

Config keys:

- `SecurityHeaders:Csp:Enabled`
- `SecurityHeaders:Csp:ReportOnly`
- `SecurityHeaders:Csp:AdditionalConnectSrc`

Only add extra `connect-src` origins that are required by the deployed frontend
or API gateway. Do not put secrets, tokens, or per-user values into CSP config.

## Destructive Migration Checklist

Treat these as destructive until proven otherwise:

- `DROP COLUMN`
- `DROP TABLE`
- data rewrite or backfill that cannot be reconstructed
- index rebuilds or long-running table locks on large tables
- migration steps that invalidate active sessions or tokens

For destructive migrations, a code revert is not a rollback plan. Rollback must
use either a verified DB restore or an explicit rollback migration/script.

## Local Development

Use the reset script when the local DB can be recreated:

```powershell
scripts/reset-dev-db.ps1
```

Or apply migrations manually:

```powershell
dotnet ef database update --project Orka.Infrastructure --startup-project Orka.API
```

Local startup auto-migration remains available only as an explicit developer
override.

## CI relational lifecycle smoke

`DataLifecycleTests` include SQL Server relational smoke coverage so FK ordering
does not silently depend on EF InMemory behavior. CI must provide one of:

- a named Windows LocalDB instance: `(localdb)\OrkaLocalDB`
- `ORKA_LIFECYCLE_SQLSERVER_BASE_CONNECTION`, a SQL Server connection string
  without a fixed database name; the test appends a disposable database name

Do not skip these tests silently. If SQL Server is unavailable, the regression
baseline should fail with a clear infrastructure error.

## Gated external provider smoke

Real provider checks are optional confidence tests, not part of the deterministic
quick baseline. Run them only in an environment that intentionally provisions a
low-cost provider secret:

```powershell
$env:ORKA_RUN_EXTERNAL_PROVIDER_TESTS="true"
$env:ORKA_EXTERNAL_GITHUB_MODELS_TOKEN="<token>"
dotnet test Orka.API.Tests\Orka.API.Tests.csproj --filter ExternalProviderIntegrationTests --no-restore --verbosity minimal
```

The tests should be kept low-volume. Missing env gates must result in an
explicit skip message and no external call.
