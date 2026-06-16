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
- The Odds API MLB current refresh can optionally include DFS/pick'em books through region `us_dfs`.
- The Odds API line-shopping uses persisted database rows only.
- MLB predictions, probability estimates, market ranking, line-shopping, and backtests exist.
- Persistent application error logging exists.
- Debug/admin routes are protected outside Development unless `DebugAdmin` is explicitly enabled with an API key.

Provider notes:

- PrizePicks support comes only through The Odds API `us_dfs` feed when configured.
- The Odds API is the desired production source of truth for odds and DFS pick'em rows.
- Direct Underdog remains supported because it works and does not spend The Odds API credits, but it is temporary/supplemental coverage, not the desired long-term production dependency.
- Use the Underdog source coverage audit to compare direct Underdog against The Odds API `us_dfs` Underdog rows, but remember the audit is cadence-sensitive. It is not an apples-to-apples comparison unless both sources run on comparable refresh intervals.
- The Odds API automated ingestion worker exists only as a guarded skeleton and is disabled by default.
- Production direction is scheduled backend The Odds refreshes plus read-only frontend endpoints. Public frontend users should not trigger provider refreshes or spend provider credits.
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
- Production-safe market freshness: `http://localhost:5299/api/markets/freshness`
- Production-safe market top picks: `http://localhost:5299/api/markets/top-picks`
- Production-safe market line-shopping: `http://localhost:5299/api/markets/line-shopping`
- Production-safe market performance: `http://localhost:5299/api/markets/performance-summary`
- Production-safe DFS entry rules: `http://localhost:5299/api/dfs/entry-rules`
- System health JSON: `http://localhost:5299/api/debug/system/health`

Do not commit real API keys. Keep local secrets in `api/appsettings.Local.json` or environment variables. Environment variables override local JSON.

## Important Config

Key config sections:

- `DebugAdmin`: protects `/research` and `/api/debug/*` outside Development.
- `UnderdogIngestion`: controls Underdog worker.
- `MlbResultImportWorker`: controls MLB result import worker.
- `TheOddsApi`: API key, base URL, sport key, regions, odds format, markets, and `MaxConcurrentRequests` for bounded parallel event-odds requests during explicit current-slate refreshes.
- `TheOddsIngestionWorker`: disabled, dry-run-first worker skeleton. Its `Bookmakers` value is the conservative sportsbook context list used by manual/current The Odds MLB refresh workflows. `ExpandedBookmakers` is an opt-in expanded US sportsbook preset used only by `bookmakerPreset=expanded` or `bookmakerPreset=all`. `DfsRegion`, `DfsBookmakers`, and `IncludeDfsBookmakers` control optional DFS/pick'em rows through The Odds API `us_dfs`.
- `TheOddsScheduledRefresh`: disabled-by-default scheduled refresh worker foundation. Defaults to `Enabled=false`, `BookmakerPreset=dfsOnly`, and `RequireForceLive=true`; the status endpoint is read-only and does not call external APIs.
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

## Production Direction

For a real product, the data strategy is provider-feed-first, not scraper-first. The Odds API or another paid/allowed provider feed should be the production source of truth. Do not plan to build custom scrapers for PrizePicks, Pick6, Betr, Sleeper, ParlayPlay, or other DFS apps.

The Odds API refreshes should run from a scheduled backend worker or admin process on a controlled cadence, roughly every 1-5 minutes depending on credit budget, slate timing, and freshness needs. The worker should refresh provider data once per interval, persist normalized rows, generate predictions, build line-shopping/top-pick outputs, and update freshness metadata.

Frontend users should only call read-only endpoints that return the latest persisted odds, predictions, line-shopping groups, top picks, and freshness state. All users should see the same latest persisted data. The UI should show last refreshed time, stale-data warnings, and game-started/expired lifecycle labels.

The current production-safe read endpoints are `/api/markets/freshness`, `/api/markets/top-picks`, `/api/markets/line-shopping`, `/api/markets/performance-summary`, and `/api/dfs/entry-rules`. They do not call provider APIs and do not write rows. `/api/mlb/*` remains as compatibility aliases for MLB-specific reads, but future frontend work should prefer `/api/markets/*` with filters instead of adding duplicated endpoint families per sport. The generic market routes currently support MLB only and report that limitation in response metadata.

The existing debug/admin `refresh-current` endpoint remains for manual controlled refreshes and requires `forceLive=true` for live provider calls. It should not be called by a public frontend.

Direct Underdog should remain configurable while it is useful for development and supplemental coverage. In production it should eventually be disabled by default once The Odds API scheduled refresh coverage is proven adequate, then removed if it is redundant.

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
- The Odds API scheduled refresh worker foundation.

Not present:

- Direct PrizePicks ingestion, scraping, worker, and health endpoints.

## Safe Operating Rules

- `/research` and `/api/debug/*` are internal/admin surfaces.
- Outside Development, debug/admin routes are blocked unless `DebugAdmin.Enabled=true` and the correct `X-Debug-Admin-Key` header is supplied.
- The Odds API calls spend credits. The dashboard must not call The Odds API on page load.
- Use dry-run before committing The Odds API ingestion.
- Live `refresh-current` calls require `forceLive=true`; calls without it are blocked before provider requests to avoid accidental credit spend.
- Expanded and All bookmaker presets are opt-in because they can spend more credits and increase refresh time.
- DFS rows from The Odds API use indicative odds and may vary based on user selections. Treat sportsbook rows as market context for a DFS-only user.
- DFS entries are provider-scoped. Do not mix PrizePicks, Underdog, DraftKings Pick6, Betr Picks, or sportsbook-context legs in one future entry builder. Entry rules marked `NeedsVerification` must be confirmed before production use.
- Generated endpoint outputs go under `tmp-analysis`; logs go under `logs`; temporary build outputs go under `tmp-build`.
- Do not add direct PrizePicks scraping, browser automation, or provider-specific bypass work.
- Do not add new direct scrapers for other DFS providers. Provider feeds are the production path.
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
