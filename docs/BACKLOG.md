# Backlog

This backlog is intentionally practical and ordered around current MLB research needs.

## Near-Term

1. Backtest The Odds line-shopping after the 2026-06-15 results import.
   - Impact: validates the first persisted standard bookmaker line-shopping loop.
   - Dependency: MLB result import for completed games.

2. Verify lifecycle behavior after the first ingested game starts.
   - Impact: ensures stale/game-started rows leave the default active recommendation view.
   - Dependency: persisted The Odds event with start time and snapshots.

3. Add a "Model Performance / If You Followed Picks" research page.
   - Impact: makes backtests understandable without raw JSON.
   - Dependency: stable line-shopping backtest output.

4. Start paper bet tracking.
   - Impact: validates recommendations prospectively, not just retrospectively.
   - Dependency: clear lifecycle state and selected recommendation rules.

5. Harden line lifecycle rules.
   - Impact: prevents stale or expired lines from being treated as active.
   - Dependency: observed timestamps and event start times.

6. Continue The Odds player mapping cleanup for remaining safe cases.
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

5. Persist probability/EV outputs if research validates them.
   - Impact: avoids recomputation and supports product UI.
   - Dependency: backtest validation.

6. Make The Odds worker production-ready.
   - Impact: keeps standard bookmaker supply fresh.
   - Dependency: credit policy, observability, retry/error handling, event selection rules.

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

- Do not revive PrizePicks scraping or DataDome bypass work.
- Do not enable The Odds worker live polling without explicit credit/ops readiness.
- Do not present EV as proven profitable.
- Do not auto-merge ambiguous players.
