# SportsBetting App Handbook

This is the living entry point for the SportsBetting app. It explains what the app does, what is automatic, what is manual, and where to find deeper documentation.

## Product Purpose

SportsBetting is an internal research system for player prop analysis. The current app focuses on MLB first:

- ingest sportsbook/player-prop markets,
- import completed MLB player results,
- generate baseline predictions and probabilities,
- compare lines across books,
- backtest recommendations after results arrive,
- surface the research in an internal dashboard.

This is not yet a public betting product. It is a research cockpit for validating data quality, model quality, market freshness, line-shopping behavior, and backtest outcomes.

## Current Status

Working today:

- Underdog ingestion worker is enabled by default.
- MLB result import worker is enabled by default.
- The Odds API MLB manual event ingest works through debug endpoints and the `/research` dashboard.
- The Odds API line-shopping uses persisted database rows only.
- MLB predictions, probability estimates, market ranking, line-shopping, and backtests exist.
- Persistent application error logging exists.
- Debug/admin routes are protected outside Development unless `DebugAdmin` is explicitly enabled with an API key.

Disabled or retired:

- PrizePicks direct ingestion is disabled and retired because of DataDome blocking. Do not attempt bypass work.
- The Odds API automated ingestion worker exists only as a guarded skeleton and is disabled by default.
- EV persistence is not implemented. EV diagnostics are research-only.

## How To Run Locally

Use the ignored local appsettings file for local secrets and real connection strings:

```powershell
cd D:\Documents\Programs\Web\SportsBetting\api
Copy-Item .\appsettings.Local.example.json .\appsettings.Local.json
notepad .\appsettings.Local.json
dotnet run --project .\SportsBetting.Api.csproj --urls "http://localhost:5299"
```

Default local base URL:

```text
http://localhost:5299
```

Useful URLs:

- API health: `http://localhost:5299/health`
- DB readiness: `http://localhost:5299/health/ready`
- Internal dashboard: `http://localhost:5299/research`
- System health JSON: `http://localhost:5299/api/debug/system/health`

Do not commit real API keys. Keep local secrets in `api/appsettings.Local.json` or environment variables. Environment variables override local JSON.

## Important Config

Key config sections:

- `DebugAdmin`: protects `/research` and `/api/debug/*` outside Development.
- `UnderdogIngestion`: controls Underdog worker.
- `MlbResultImportWorker`: controls MLB result import worker.
- `TheOddsApi`: API key, base URL, sport key, regions, odds format, markets, and `MaxConcurrentRequests` for bounded parallel event-odds requests during explicit current-slate refreshes.
- `TheOddsIngestionWorker`: disabled, dry-run-first worker skeleton. Its `Bookmakers` value is also the config-driven list used by manual/current The Odds MLB refresh workflows.
- `PredictionGeneration`: recommendation thresholds and enabled prop types.

## Major Workflows

The normal MLB research loop is:

1. Fetch The Odds API MLB events manually from `/research`.
2. Dry-run a selected event ingest.
3. Commit event ingest only after confirming credit/write intent.
4. Generate scoped The Odds MLB predictions.
5. Refresh line-shopping.
6. Wait for MLB games to finish.
7. Let MLB result import worker import completed games, or use result import debug endpoints manually.
8. Run line-shopping/backtest diagnostics.

See [WORKFLOWS.md](WORKFLOWS.md) for command-level details.

## Automatic Vs Manual

Automatic:

- Underdog ingestion worker.
- MLB result import worker.
- Database migrations are applied during startup when pending migrations exist.
- Application errors are persisted through middleware and worker logging.

Manual:

- The Odds API event fetch and event ingest.
- The Odds scoped prediction generation from the dashboard/debug endpoint.
- Player mapping/alias approvals.
- Backtest/audit endpoints.

Disabled by default:

- The Odds API automated ingestion worker.
- PrizePicks ingestion.

## Safe Operating Rules

- `/research` and `/api/debug/*` are internal/admin surfaces.
- Outside Development, debug/admin routes are blocked unless `DebugAdmin.Enabled=true` and the correct `X-Debug-Admin-Key` header is supplied.
- The Odds API calls spend credits. The dashboard must not call The Odds API on page load.
- Use dry-run before committing The Odds API ingestion.
- Generated endpoint outputs go under `tmp-analysis`; logs go under `logs`; temporary build outputs go under `tmp-build`.
- Do not bypass DataDome or revive PrizePicks scraping experiments.
- Do not treat EV outputs as production recommendations until backtests support them.
- Non-standard market types should be visible as research rows, but not mislabeled as normal recommendations.

## Deeper Docs

- [API_ENDPOINTS.md](API_ENDPOINTS.md): debug/admin and health endpoints.
- [ARCHITECTURE.md](ARCHITECTURE.md): backend, dashboard, providers, workers, pipelines.
- [WORKFLOWS.md](WORKFLOWS.md): operational procedures.
- [DATA_MODEL.md](DATA_MODEL.md): important tables and identity concepts.
- [DECISIONS.md](DECISIONS.md): durable technical/product decisions.
- [BACKLOG.md](BACKLOG.md): prioritized future work.
- [DOCUMENTATION_GUIDE.md](DOCUMENTATION_GUIDE.md): when docs must be updated.
- [GENERATED_OUTPUTS.md](GENERATED_OUTPUTS.md): where generated logs and JSON outputs belong.
- [SECRETS_AND_SOURCE_CONTROL.md](SECRETS_AND_SOURCE_CONTROL.md): private repo and secret handling rules.
