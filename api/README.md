# SportsBetting API

Initial ASP.NET Core 10 backend for the Sports Pick'em Analyzer.

## Prerequisites

- .NET SDK 10
- PostgreSQL 15 or newer
- EF Core CLI tool, restored from the local tool manifest

## Database

Create a local PostgreSQL database and user using credentials of your choice:

```sql
CREATE USER APP_DATABASE_USER WITH PASSWORD 'APP_DATABASE_PASSWORD';
CREATE DATABASE sports_betting OWNER APP_DATABASE_USER;
```

Committed appsettings files intentionally use this placeholder:

```text
POSTGRES_CONNECTION_STRING
```

Use a gitignored local override for your real local connection string:

```powershell
Copy-Item .\appsettings.Local.example.json .\appsettings.Local.json
notepad .\appsettings.Local.json
```

Do not commit `appsettings.Local.json`. Environment variables can still be used for automation and override local JSON.

## Restore And Build

```powershell
dotnet tool restore
dotnet restore
dotnet build SportsBetting.Api.slnx
```

## Migrations

Restore the local EF Core CLI:

```powershell
dotnet tool restore
```

Apply the initial migration:

```powershell
dotnet tool run dotnet-ef database update
```

Create future migrations:

```powershell
dotnet tool run dotnet-ef migrations add MigrationName
```

If the local tool shim is unavailable on your machine, the direct package invocation also works after `dotnet tool restore`:

```powershell
dotnet "$env:USERPROFILE\.nuget\packages\dotnet-ef\10.0.9\tools\net8.0\any\dotnet-ef.dll" database update
```

## Run

```powershell
dotnet run --project .\SportsBetting.Api.csproj --urls "http://localhost:5299"
```

Health endpoints:

- `GET /health`
- `GET /health/ready`
- `GET /api/health/prizepicks-ingestion`
- `GET /api/health/underdog-ingestion`

The ingestion health endpoints return the last successful ingestion time, last ingestion duration, last ingestion status, failure count, last error message, and whether the worker is currently running.

## Project Documentation

The living app docs are in the root `docs` folder:

- `docs/APP_HANDBOOK.md` - app overview, current status, safe operating rules.
- `docs/API_ENDPOINTS.md` - debug/admin and health endpoint catalog.
- `docs/ARCHITECTURE.md` - backend, providers, workers, prediction, line-shopping, backtesting.
- `docs/WORKFLOWS.md` - local operations and research workflows.
- `docs/DATA_MODEL.md` - important tables and identity concepts.
- `docs/DECISIONS.md` - durable product/engineering decisions.
- `docs/BACKLOG.md` - prioritized next work.
- `docs/DOCUMENTATION_GUIDE.md` - when docs must be updated.
- `docs/SECRETS_AND_SOURCE_CONTROL.md` - private repo and secret handling rules.

The internal research dashboard is available locally at:

```text
http://localhost:5299/research
```

`/research` and `/api/debug/*` are internal/admin surfaces. Outside Development they are blocked unless `DebugAdmin` is explicitly enabled and the correct admin key is provided.

Deprecated PrizePicks direct ingestion endpoints scheduled for removal:

- `GET /api/debug/prizepicks`
- `POST /api/debug/prizepicks/ingest`

PrizePicks direct ingestion is deprecated and scheduled for complete removal. These endpoints return a disabled response and do not call PrizePicks.

PrizePicks background ingestion is not configured through committed appsettings. Do not add active PrizePicks config or new dependencies on the old direct ingestion path. PrizePicks support should come through The Odds API `us_dfs`.

## Underdog Background Ingestion Worker

Underdog background ingestion runs every 5 minutes by default:

```json
"UnderdogIngestion": {
  "Enabled": true,
  "IntervalMinutes": 5
}
```

## League Configuration

Ingestion should collect and store all available leagues. League configuration is only for downstream analysis, optimizer, and display workflows.

The `league_configurations` table includes:

- `league_name`
- `is_enabled_for_analysis`
- `is_enabled_for_optimizer`
- `is_enabled_for_display`

Seeded as enabled by default:

- NFL
- NBA
- WNBA
- NHL
- MLB
- SOCCER
- UFC

All newly added or unseeded leagues default to disabled for analysis, optimizer, and display.

## Current Scope

Implemented:

- ASP.NET Core 10 Web API
- EF Core with PostgreSQL provider
- Application DbContext
- Entity models based on `docs/ARCHITECTURE.md`
- Initial migration
- Health endpoint
- PrizePicks ingestion spike debug endpoint
- PrizePicks persistence debug endpoint
- PrizePicks background ingestion worker
- PrizePicks ingestion worker health endpoint
- Underdog ingestion worker health endpoint
- League configuration table for downstream filtering

Not implemented yet:

- Probability engine
- Correlation engine
- Frontend
