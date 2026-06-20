# Frontend Read API Wiring

The authenticated customer frontend reads production-safe persisted market data through the public read-only API routes. These endpoints do not call provider APIs and do not write rows.

## Local Configuration

Set the frontend API base URL with:

```powershell
$env:VITE_API_BASE_URL="http://localhost:5299"
```

If the variable is not set, the frontend defaults to `http://localhost:5299`.

The API allows local Vite development origins through a narrow CORS policy:

- `http://localhost:5173` through `http://localhost:5179`
- `https://localhost:5173` through `https://localhost:5179`
- `http://127.0.0.1:5173` through `http://127.0.0.1:5179`
- `https://127.0.0.1:5173` through `https://127.0.0.1:5179`

## Wired Endpoints

- `GET /api/markets/freshness`
- `GET /api/markets/provider-status`
- `GET /api/markets/top-picks`
- `GET /api/markets/line-shopping`
- `GET /api/markets/performance-summary`
- `GET /api/players/search`
- `GET /api/player-research/current-lines`

The frontend has a central API client under `frontend/src/api/`. The client already calls a `getAccessToken()` stub before requests. It returns `null` for now, but it is the future attachment point for an `Authorization: Bearer <token>` header when real auth is connected.

## Current Page Coverage

- Dashboard uses freshness, top picks, line-shopping preview rows, and performance summary.
- Top Picks uses `/api/markets/top-picks`.
- Line Shopping uses `/api/markets/line-shopping`.
- Entry Builder candidate browsing uses `/api/markets/top-picks` while selected legs remain shared in-memory React state.
- Performance uses `/api/markets/performance-summary`.
- Player Research uses `/api/players/search` for persisted player autocomplete, `/api/player-research/current-lines` for selected-player current provider lines with safe equivalent identity resolution, and `/api/markets/prop-history` for persisted recent stat history.
- App topbar uses `/api/markets/freshness`.

## Known Gaps

Markets still use persisted scanner/comparison endpoints for public line inventory. Player Research now has a selected-player current-line endpoint that resolves only safe equivalent identities, so duplicate persisted player rows such as MLB/MLBLIVE variants do not hide current provider lines. It still does not create player rows or call external providers on demand.

Account, Billing, and user-specific pick tracking remain mock/static for this pass. Player Research search is database-backed, but it only returns players already persisted by ingestion/import workflows; it does not call external providers or create player rows on demand. Future protected user endpoints must require JWT auth before they persist user selections, billing state, or account settings.
