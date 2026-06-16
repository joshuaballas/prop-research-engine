# PrizePicks Direct Ingestion Disabled

PrizePicks direct ingestion is disabled due to DataDome blocking.

Do not attempt DataDome bypass, browser scraping, or browser-automation workarounds.

PrizePicks can be researched through The Odds API DFS feed instead:

- region: `us_dfs`
- bookmaker key: `prizepicks`
- endpoint path: `POST /api/debug/the-odds/mlb/recommendations/refresh-current?includeDfs=true`

DFS odds from provider feeds are indicative and can vary based on user selections. Keep the old direct PrizePicks scraper/API/browser paths retired.

Revisit direct PrizePicks ingestion only if an official integration, paid provider, user-provided export, or otherwise allowed stable data source is available.
