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

- Direct Underdog ingestion is disabled by default and retained only for explicit debug/audit checks.
- MLB result import worker is enabled by default.
- The Odds API MLB manual event ingest works through debug endpoints and the `/research` dashboard.
- The Odds API MLB current refresh can optionally include DFS/pick'em books through region `us_dfs`.
- The Odds API line-shopping uses persisted database rows only.
- Provider line lifecycle tracking records first seen, last seen, movement, stale, pulled, locked, and expired status after successful The Odds refresh snapshots.
- MLB predictions, probability estimates, market ranking, line-shopping, and backtests exist.
- Persistent application error logging exists.
- Debug/admin routes are protected outside Development unless `DebugAdmin` is explicitly enabled with an API key.

Provider notes:

- PrizePicks, Underdog, DraftKings Pick6, and Betr Picks support comes through The Odds API `us_dfs` feed when configured.
- The Odds API is the active production source of truth for odds and DFS pick'em rows.
- Direct Underdog is debug/audit only. It should not run automatically or feed customer-facing `/api/markets/*` reads.
- Use the Underdog source coverage audit only for explicit comparisons against The Odds API `us_dfs` Underdog rows.
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
- Production-safe provider status: `http://localhost:5299/api/markets/provider-status`
- Production-safe refresh plan: `http://localhost:5299/api/markets/refresh-plan`
- Production-safe market top picks: `http://localhost:5299/api/markets/top-picks`
- Production-safe market line-shopping: `http://localhost:5299/api/markets/line-shopping`
- Production-safe market performance: `http://localhost:5299/api/markets/performance-summary`
- Production-safe DFS entry rules: `http://localhost:5299/api/dfs/entry-rules` exposes explicitly supported DFS pick'em entry providers separately from sportsbook context-only rows. Sportsbook lines are comparison/reference data only. Optional payout multiplier tables are app-maintained configured data keyed by provider, entry type, and leg count. PrizePicks standard Player Pick top-payout multipliers are configured for Power 2-6 picks and Flex 3-6 picks and marked standard-only; partial-hit Flex payouts and special/unknown projection variations are not modeled as estimated payout rows. Underdog, DraftKings Pick6, and Betr Picks currently expose payout-model notes but no configured multiplier rows because exact official payout tables were not verified. Market rows expose projection-safety metadata from The Odds persisted fields, including alternate-market and payout-may-differ flags. If a multiplier is missing or the selected legs include special/alternate/unknown projection types, Entry Builder shows payout unavailable. Final entry availability, terms, and payout must be confirmed in the provider app.
- System health JSON: `http://localhost:5299/api/debug/system/health`
- Persistent application error logs: `http://localhost:5299/api/debug/application-errors`

Do not commit real API keys. Keep local secrets in `api/appsettings.Local.json` or environment variables. Environment variables override local JSON.

## Important Config

Configuration is split by ownership:

- Bootstrap config stays in appsettings/environment/user-secrets: connection strings, host/logging, provider base URLs needed before DB access, and security-sensitive debug/admin bootstrap.
- Secrets stay in user-secrets, environment variables, deployment-time app settings, or future Azure Key Vault-linked variable groups. `AppConfigs` is not a secret store and must not contain plaintext API keys, tokens, passwords, webhook signing secrets, encryption keys, or connection strings.
- Non-secret scalar operational/runtime knobs live in the DB-backed `AppConfigs` table. Startup seeds missing scalar rows idempotently from catalog defaults as `Environment=Global`, seeds explicit Development overrides from the catalog, and preserves DB-customized values. Effective reads prefer current-environment override, then `Global`, then appsettings fallback, then code defaults. Invalid DB values are reported and fall back safely.
- Structured business/config data belongs in domain/model tables, not JSON/blob AppConfig rows. Provider competitions, provider targets, bookmakers, markets, and other identity-bearing provider collections live in `provider_competitions`, `provider_markets`, `provider_bookmakers`, and `provider_refresh_targets`; target refresh cadence lives on `provider_refresh_targets.refresh_interval_minutes`. Model eras and thresholds live in `model_config_sets` and `model_config_parameters`. Migrated paths are classified as `MovedToDomainTable` or `MovedToModelConfig`.
- Advanced/internal settings are marked in the catalog/debug output with operator metadata: display name, short display name, section, help text, input type, unit, min/max, advanced/hidden flags, impact level, and sort order. The legacy The Odds ingestion worker and direct Underdog ingestion are hidden by default.

Read-only diagnostics:

- `/api/debug/app-configs/audit`: appsettings inventory and classification. Default counts are parent rows only; `includeChildren=true` exposes child audit rows and separate child/total counts.
- `/api/debug/app-configs`: effective visible scalar `MoveToAppConfigs` values plus operator metadata by default. It exposes `configuredValue`, `effectiveValue`, `defaultValue`, `displayValue`, and `isValueMasked`. Audit candidates, hidden/advanced rows, and masked secret metadata require explicit query flags.
- `/api/debug/app-configs/status`: cache status, global/default row counts, environment override counts, advanced/hidden counts, default audit classification counts, and validation issues.
- `/api/debug/provider-domain-config/status`: compact read-only status for normalized provider/domain configuration. It includes `appsettingsFallbackCount` as an invocation count plus compact `appsettingsFallbacks` details with key/config area/consumer/reason. Companion endpoints list competitions, markets, bookmakers, and refresh targets. These endpoints never call providers and never expose secrets.
- `/api/debug/model-config/status`: compact read-only status for model configuration. `/api/debug/model-config?includeParameters=true` lists active model sets and typed parameter rows. These endpoints never call providers and never change model behavior.

Key config sections:

- `DebugAdmin`: protects `/research` and `/api/debug/*` outside Development.
- `UnderdogIngestion`: controls the direct Underdog debug/audit worker. It is disabled by default and should not be enabled as a customer-facing provider source.
- `MlbResultImportWorker`: controls MLB result import worker. Normal startup uses `RecentWindowDays` default `14`; old historical backfill is disabled by default with `HistoricalBackfillEnabled=false`, and old non-complete retries are capped by `HistoricalNonCompleteAttemptCap`.
- `SoccerResultImportWorker`: controls the API-Sports Football Soccer result/stat import worker. Base config keeps it disabled; Development enables it with a 60-minute cadence, recent-window import, `Competition=AllSupported`, `MaxFixturesPerCompetitionPerRun=8`, `MaxFixturesTotalPerRun=40`, and API-Sports config gating. It imports World Cup 2026 plus enabled `provider_competitions` rows such as EPL, La Liga, Bundesliga, Serie A, Ligue 1, MLS, and Champions League. Recurring leagues use stable provider-domain metadata only; provider seasons/windows are resolved from cached API-Sports `/leagues` metadata (`ApiSports:Football:SeasonMetadataCacheHours`, default `24`). World Cup 2026 remains fixed to league `1`, season `2026`; the old scalar World Cup override keys are no longer supported. The scheduled worker only calls fixture/stat endpoints when a competition is active on the cycle date or has persisted completed-fixture backlog. Out-of-season competitions without backlog are skipped with `OutsideProviderSeasonWindow`; missing season metadata is skipped with `SeasonMetadataUnavailable`; status endpoints expose these as `lastRunSkipped=true` and `lastRunFailed=false` when no exception occurred. It writes idempotent `prop_results`, excludes already-imported fixtures before caps, preserves retryable max-limit deferrals, and persists worker/fixture diagnostics in `soccer_result_import_states`, `soccer_result_import_fixture_states`, and `api_sports_soccer_season_metadata`.
- `TheOddsApi`: API key, base URL, sport key, regions, odds format, markets, and `MaxConcurrentRequests` for bounded parallel event-odds requests during explicit current-slate refreshes.
- `TheOddsIngestionWorker`: disabled, dry-run-first worker skeleton. The provider-domain bookmaker rows replace comma-string ownership for the conservative sportsbook list, expanded sportsbook list, and DFS bookmaker list.
- `TheOddsScheduledRefresh`: scheduled refresh worker foundation. Global/default AppConfigs stay production-safe with `Enabled=false`, `BookmakerPreset=dfsAll`, `RequireForceLive=true`, and `AllowScheduledLiveRefresh=false`; Development AppConfigs enable the approved local MLB DFS (`dfsAll`), MLB Sportsbook (`allSportsbooks`), and Soccer World Cup Sportsbook (`allSportsbooks`) live-refresh switches. Phase 1 target cadence is stored in `provider_refresh_targets.refresh_interval_minutes`: MLB DFS 15 minutes, MLB Sportsbook 15 minutes, Soccer World Cup Sportsbook 30 minutes, and disabled Soccer league placeholders 30 minutes. Runtime planning reads DB rows first and appsettings only as fallback, then runs only targets whose own interval is due. Sportsbook scheduled rows remain non-Entry-Builder. This worker refreshes The Odds market/provider-context rows only; Soccer API-Sports outcomes/player stats are imported by the separate `SoccerResultImportWorker`. `GET /api/debug/soccer/results/api-sports/scheduled-import-status` reports that worker's per-competition state and persisted coverage; the World Cup route remains a compatibility filter. The status endpoint is read-only and does not call external APIs. When enabled, the worker sleeps until the earliest target's next eligible time, then runs a guarded planning cycle; each live attempt still consults the read-only refresh planner first. `AllowEventDiscoveryWhenNoPersistedEvents=true` allows the scheduler to use `FallbackEstimatedApiCalls` for budget checks and discover events live instead of skipping solely because no persisted slate events exist. The customer frontend does not show scheduled countdown/checking/syncing text; market pages quietly poll persisted read endpoints in the background.
- `TheOddsRefreshPlanner`: planning/budget configuration for `/api/markets/refresh-plan`. It estimates cadence and credit budget from persisted metadata only and does not enable scheduled live refreshes. Manual reads are read-only by default; scheduled worker planning cycles persist audit rows in `the_odds_refresh_planner_decisions`. `EnforcementEnabled=false` by default, so local daily/monthly budgets, provider remaining-credit estimates, and per-run estimate caps are diagnostics/warnings only. Planner responses report enforcement flags, provider-reported usage/remaining credits, telemetry staleness, `plannerWouldBlockIfEnforced`, and `plannerWarnings`. Normal successful refreshes use the configured 15-minute scheduled cadence, while a failed latest live attempt after the last success uses `RetryAfterFailureMinutes=1` so the worker can recover hands-off without waiting a full cadence. Rejected keys, provider unavailability, and recent provider quota/rate-limit failure backoff still block live calls. Provider-credit and per-run estimate blocks require `EnforcementEnabled=true` plus their specific block flags. `StopAfterEventStart=false` by default so event start affects DFS entry actionability, not whether provider polling/lifecycle tracking can continue.
- `PublicMarkets`: customer-facing read-state settings. `ColdStartGraceMinutes` defaults to 5 minutes and lets `/api/markets/freshness.marketReadiness` report `Syncing` or `Partial` during normal API startup/first sync instead of a hard red issue. It does not mask provider key rejection, provider unavailable states, credit exhaustion, enabled target misconfiguration, or failures after the grace window.
- `ProviderLineLifecycle`: line lifecycle thresholds. Defaults keep active lines fresh for 15 minutes, mark pulled after 2 successful missing refreshes, and expire old event lines after 12 hours.
- `PredictionGeneration`: current thresholds and enabled prop types are model configuration parameters, not committed appsettings values.

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

The Odds API refreshes should run from a scheduled backend worker or admin process on controlled per-target cadences based on observed lifecycle behavior, credit budget, target availability, and freshness needs. Current dev guidance is scheduler off by default in base config; local Development may enable MLB DFS at 15 minutes, MLB Sportsbook at 15 minutes, and Soccer World Cup Sportsbook at 30 minutes through DB-backed provider refresh targets. The worker should refresh only due targets, persist normalized rows, generate predictions where the sport/category supports them, build line-shopping/top-pick outputs, update freshness metadata, and update provider line lifecycle state.

Refreshability is not the same as actionability. Provider polling may continue after event/game start so the app can track line movement, pulled lines, stale lines, locked rows, and provider availability. Event start can still make a DFS line non-actionable for Entry Builder/Add Leg even when the data remains visible and refreshed.

Frontend users should only call read-only endpoints that return the latest persisted odds, predictions, line-shopping groups, top picks, and freshness state. All users should see the same latest persisted data. Customer pages should refresh those persisted reads quietly in the background and keep prior data on screen while refetching; scheduled-refresh countdown/checking text should not be part of the customer top bar. The top bar should use `/api/markets/freshness.marketReadiness`: green for `Fresh/ok`, amber/plain syncing text for `Syncing`, `Partial`, or warning states, and red refresh issue only for `Failed`, `ProviderUnavailable`, or error severity. Market polling is one request per active visible scope: selected MLB/Soccer views request that sport only, while mixed boards use a single all-sports read (`/api/markets/top-picks` with no sport and `/api/markets/line-shopping?sport=All`) instead of frontend per-sport fanout. The Performance page is not a live market board; it should fetch on mount, sport/filter change, refresh-version change, or window refocus, not on the 15-second market polling cadence.

The current production-safe read endpoints are `/api/markets/freshness`, `/api/markets/top-picks`, `/api/markets/line-shopping`, `/api/markets/performance-summary`, and `/api/dfs/entry-rules`. They do not call provider APIs and do not write rows. `/api/mlb/*` remains as compatibility aliases for MLB-specific reads, but future frontend work should prefer `/api/markets/*` with filters instead of adding duplicated endpoint families per sport. Generic market routes support MLB and guarded Soccer / World Cup reads; World Cup remains `sport=SOCCER&competition=World Cup`. Top Picks are controlled by the DB-backed `TopPicksModel` model config set, default active model `LikelyHitV1`, and are the only public surface where recommendation promotion gates control visibility. Prop Scanner, Prop Board, and Line Shopping are inventory/context pages and must not block valid current rows because they are not Top Picks. Performance summary is based on the durable `recommendation_snapshots` ledger, not the generic current read snapshots. Performance accepts `sport=All` for server-side multi-sport aggregation and exposes per-sport breakdowns; Soccer / World Cup grades only persisted API-Sports Goals, Assists, Shots, and Shots On Target rows. Performance summary responses are persisted-read derived and cached for 15 minutes per full query key because result/grading data changes on result imports, not every odds refresh. The Odds refresh writes ledger rows for surfaced candidate/watchlist/top-pick opportunities with canonical player id and expected result date, while `market_top_pick_snapshots` and `market_line_shopping_snapshots` remain current UI read models. Performance summary supports `gradingMode=latestCompleted` for customer Dashboard-style summaries, `gradingMode=latestGraded` for the newest safely matched slate, and `gradingMode=currentSlate` for pending/current slate visibility on deeper diagnostics. Its `yesterday` fields are latest-graded-slate scoped, while its `overall` fields aggregate all current-era graded opportunities for the selected filters and expose `uniqueGradedDateCount`, `gradedOpportunitiesByDate`, and `overallDateRange`. When MLB result import coverage states exist, partial or missing result dates are skipped so Dashboard-style performance does not headline hit rate/ROI from an incomplete import. Pending, no-result, and unmatched picks are reported separately and are not counted in wins, losses, hit rate, or ROI. `NoResult` means the provider had a line but the completed sport import has no gradeable player/prop result row; `Unmatched` means identity, prop type, or date matching is ambiguous. Customer Dashboard performance should use the latest complete imported result date; it should not foreground current-day pending candidates or partial result imports.

The existing debug/admin `refresh-current` endpoint remains for manual controlled refreshes and requires `forceLive=true` for live provider calls. It should not be called by a public frontend.

Direct Underdog remains configurable only for explicit debug/audit work. The Odds API `us_dfs` is the active provider source for customer-facing rows, including Underdog.

## Automatic Vs Manual

Automatic:

- The Odds scheduled refresh worker when explicitly enabled by config.
- MLB result import worker.
- Database migrations are applied during startup when pending migrations exist.
- Unexpected API and worker/job errors are persisted through middleware and targeted worker logging. Error-log debug endpoints are read-only and do not call providers.

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
- Provider/API failure does not mean provider-pulled line. Lifecycle rows become `Pulled` only when a line is missing from successful provider refreshes according to threshold.
- Use `/api/debug/provider-lines/lifecycle-summary` and `/api/debug/provider-lines/refresh-cadence-insights` before lowering refresh interval or upgrading provider plan.
- Expanded and All bookmaker presets are opt-in because they can spend more credits and increase refresh time.
- DFS rows from The Odds API use indicative odds and may vary based on user selections. Treat sportsbook rows as market context for a DFS-only user.
- DFS entries are provider-scoped. Do not mix PrizePicks, Underdog, DraftKings Pick6, Betr Picks, or sportsbook-context legs in one future entry builder. Entry rules marked `NeedsVerification` must be confirmed before production use.
- Generated endpoint outputs go under `tmp-analysis`; logs go under `logs`; temporary build outputs go under `tmp-build`.
- Do not add direct PrizePicks scraping, browser automation, or provider-specific bypass work.
- Do not add new direct scrapers for other DFS providers. Provider feeds are the production path.
- Do not treat EV as the primary Top Picks signal. Likely-Hit v1 recommendations are hit-probability-first, but default Top Picks must still be playable: sportsbook rows below the Balanced favorite cutoff are inventory only, not public Top Picks. Public Top Picks is an opportunity-level curated board, not a provider-line fanout board; provider/bookmaker alternatives belong in `providerOptions`, the stable `propFamily` field explains player+prop-family exposure caps, and Prop Scanner and Line Shopping remain the broad market-inventory views. Exposure caps are output/product controls, not model scoring. Sportsbook EV is displayed only when real price context and sanity gates make it safe. Use `/api/debug/model/likely-hit/value-audit` for read-only value diagnostics that separate estimated EV, flat even-money ROI, logged-price-adjusted ROI, compact curation summaries, broad/pre-cap provider-fanout diagnostics, final-public-board fanout diagnostics, and broad-vs-curated board curation without changing public model behavior.
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
