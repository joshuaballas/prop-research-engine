# Backlog

This backlog is intentionally practical and ordered around current MLB research needs.

## Near-Term

1. Implement scheduled The Odds refresh worker and production read-only endpoints.
   - Impact: prevents frontend users from spending The Odds API credits and gives all users the same latest persisted odds, predictions, line-shopping groups, top picks, and freshness metadata.
   - Dependency: current debug refresh workflow, force-live safety gate, lifecycle metadata, and agreed refresh cadence/provider preset.
   - Target: configurable 1-5 minute cadence depending on credit budget, slate timing, and freshness needs.

2. Prove The Odds scheduled Underdog DFS coverage against direct Underdog.
   - Impact: determines whether The Odds API scheduled refreshes can replace direct Underdog as the production source.
   - Dependency: scheduled The Odds refresh worker running on comparable intervals to direct Underdog.
   - Note: one-off manual/debug audits are cadence-sensitive and should not be treated as apples-to-apples production comparisons.

3. Backtest The Odds line-shopping after the 2026-06-15 results import.
   - Impact: validates the first persisted standard bookmaker line-shopping loop.
   - Dependency: MLB result import for completed games.

4. Verify lifecycle behavior after the first ingested game starts.
   - Impact: ensures stale/game-started rows leave the default active recommendation view.
   - Dependency: persisted The Odds event with start time and snapshots.

5. Add a "Model Performance / If You Followed Picks" research page.
   - Impact: makes backtests understandable without raw JSON.
   - Dependency: stable line-shopping backtest output.

6. Start paper bet tracking.
   - Impact: validates recommendations prospectively, not just retrospectively.
   - Dependency: clear lifecycle state and selected recommendation rules.

7. Harden line lifecycle rules.
   - Impact: prevents stale or expired lines from being treated as active.
   - Dependency: observed timestamps and event start times.

8. Continue The Odds player mapping cleanup for remaining safe cases.
   - Impact: improves prediction coverage.
   - Dependency: candidate endpoints and manual review.

## Medium-Term

1. Build a real frontend/research product shell.
   - Impact: moves beyond internal debug dashboard.
   - Dependency: stable data contracts.

2. Add market-history-aware backtesting.
   - Impact: reconstructs predictions against lines observed before game start.
   - Dependency: reliable `prop_line_snapshots`.

3. Improve model features.
   - Candidate features: opponent strength, home/away splits, pitcher/batter handedness, lineup context, park factors, role/playing-time signals, recent variance.
   - Impact: improves probability calibration and ranking.

4. Add model validation breakdowns to the dashboard.
   - Impact: quickly spots bad prop types/books/market classes.
   - Dependency: completed backtests.

5. Separate DFS leg-level performance from sportsbook pick performance.
   - Impact: prevents customer-facing Performance metrics from mixing DFS pick'em leg outcomes with true sportsbook straight-pick outcomes.
   - Dependency: provider-category filtering in `performance-summary` / `performance-results`, sportsbook-context ingestion/results, and clear frontend labeling.
   - UX direction: split Performance into DFS Leg Results / DFS Leg History and Sportsbook Pick Results / Sportsbook Pick History. DFS copy should explain that rows are individual recommendation outcomes and not entry-level performance because DFS entries require multiple legs. Sportsbook copy should explain that sportsbook recommendations can be evaluated as single picks using simulated straight-bet results.
   - Backend direction: support `providerCategory=DFS|Sportsbook` filters. DFS metrics should be labeled as leg-level performance. Sportsbook metrics should use sportsbook odds/prices for straight-pick W/L and simulated unit ROI.
   - Guardrails: do not fake sportsbook stats from DFS lines; do not build DFS entry-level ROI until tracked entry construction data exists; show a neutral sportsbook empty/future state until sportsbook ingestion/results are enabled.

6. Persist probability/EV outputs if research validates them.
   - Impact: avoids recomputation and supports product UI.
   - Dependency: backtest validation.

7. Make The Odds worker production-ready.
   - Impact: keeps sportsbook context and DFS pick'em supply fresh through the provider feed.
   - Dependency: credit policy, observability, retry/error handling, event selection rules.

8. Disable direct Underdog by default once The Odds scheduled coverage is sufficient.
   - Impact: moves production toward a single provider-feed source of truth while retaining direct Underdog only when explicitly configured.
   - Dependency: comparable scheduled coverage audit and acceptable freshness/market coverage.

9. Remove direct Underdog if The Odds scheduled coverage makes it redundant.
   - Impact: reduces custom direct-source maintenance.
   - Dependency: sustained evidence that The Odds API `us_dfs` Underdog coverage is adequate for production needs.

## Later

1. Add additional sports.
   - Candidate order: NFL, NBA, WNBA, NHL, UFC/MMA.
   - Dependency: result source, player mapping, prop-type support, backtest coverage.

2. Add additional odds providers or sportsbooks.
   - Impact: improves line-shopping depth.
   - Dependency: clean JSON API, pricing/terms review.

3. Add public-safe API surface.
   - Impact: separates product routes from debug/admin routes.
   - Dependency: authentication and authorization design.

4. Add user accounts, saved filters, and watchlists.
   - Impact: productization.
   - Dependency: validated recommendation UX.

5. Add ROI/bankroll tooling.
   - Impact: betting workflow support.
   - Dependency: reliable odds, lifecycle, and grading.

## Do Not Do Yet

- Do not add direct PrizePicks scraping, browser automation, or provider-specific bypass work.
- Do not build direct scrapers for Pick6, Betr, PrizePicks, Sleeper, ParlayPlay, or other DFS apps.
- Do not treat direct Underdog as the final production data dependency.
- Do not let public frontend routes trigger The Odds API provider refreshes.
- Do not enable The Odds worker live polling without explicit credit/ops readiness.
- Do not present EV as proven profitable.
- Do not auto-merge ambiguous players.
