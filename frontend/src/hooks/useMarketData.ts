import { useMemo } from 'react';
import { getLineShopping, getMarketFreshness, getPerformanceResults, getPerformanceSummary, getProviderStatus, getScheduledRefreshStatus, getTopPicks } from '../api/markets';
import type { PerformanceGradingMode } from '../api/markets';
import { mapMarketGroupsToOpportunities, mapPerformanceSummaryToGroups } from '../api/marketMappers';
import { useApiQuery } from '../api/useApiQuery';
import { isDemoDataMode } from '../config/dataMode';
import {
  demoEntryCandidates,
  demoLineShopping,
  demoMarketFreshness,
  demoMarketScanner,
  demoPerformance,
  demoProviderStatus,
  demoScheduledRefreshStatus,
  demoTopPicks
} from '../data/demo/marketFixtures';
import type { MarketOpportunity } from '../types/markets';
import { useMarketDataRefreshVersion } from './useMarketDataRefresh';

const MARKET_DATA_POLL_INTERVAL_MS = 15000;
const MARKET_QUERY_OPTIONS = { refetchIntervalMs: MARKET_DATA_POLL_INTERVAL_MS };
export type MarketReadSport = 'All' | 'MLB' | 'SOCCER';

export function useMarketFreshnessData(deps: readonly unknown[] = []) {
  const refreshVersion = useMarketDataRefreshVersion();
  return useApiQuery((signal) => (isDemoDataMode ? Promise.resolve(demoMarketFreshness) : getMarketFreshness(signal)), [...deps, refreshVersion], MARKET_QUERY_OPTIONS);
}

export function useProviderStatusData(deps: readonly unknown[] = []) {
  const refreshVersion = useMarketDataRefreshVersion();
  return useApiQuery((signal) => (isDemoDataMode ? Promise.resolve(demoProviderStatus) : getProviderStatus(signal)), [...deps, refreshVersion], MARKET_QUERY_OPTIONS);
}

export function useScheduledRefreshStatusData(deps: readonly unknown[] = []) {
  const refreshVersion = useMarketDataRefreshVersion();
  return useApiQuery((signal) => (isDemoDataMode ? Promise.resolve(demoScheduledRefreshStatus) : getScheduledRefreshStatus(signal)), [...deps, refreshVersion], MARKET_QUERY_OPTIONS);
}

export function useTopPicksData(take = 30, sourcePrefix = 'top-pick', sport: MarketReadSport = 'All') {
  const refreshVersion = useMarketDataRefreshVersion();
  const query = useApiQuery(
    async (signal) => {
      if (isDemoDataMode) {
        return mapDemoMarketsToOpportunities(demoTopPicks.slice(0, take), sourcePrefix);
      }

      const response = await getTopPicks(signal, take, normalizeTopPicksReadSport(sport));
      return mapMarketGroupsToOpportunities(response.topPicks, sourcePrefix);
    },
    [take, sourcePrefix, sport, refreshVersion],
    MARKET_QUERY_OPTIONS
  );
  const markets = query.data ?? [];

  return { ...query, markets };
}

export function useLineShoppingData(take = 50, topOnly = false, sourcePrefix = 'line-shopping', sport: MarketReadSport = 'All') {
  const refreshVersion = useMarketDataRefreshVersion();
  const query = useApiQuery(
    async (signal) => {
      if (isDemoDataMode) {
        return mapDemoMarketsToOpportunities(demoLineShopping.slice(0, take), sourcePrefix);
      }

      const response = await getLineShopping(signal, take, topOnly, normalizeLineShoppingReadSport(sport));
      return mapMarketGroupsToOpportunities(response.comparisonGroups, `${sourcePrefix}-${sport.toLowerCase()}`);
    },
    [take, topOnly, sourcePrefix, sport, refreshVersion],
    MARKET_QUERY_OPTIONS
  );
  const markets = query.data ?? [];

  return { ...query, markets };
}

export function useMarketScannerData(take = 75, sourcePrefix = 'market', sport: MarketReadSport = 'All') {
  const refreshVersion = useMarketDataRefreshVersion();
  const query = useApiQuery(
    async (signal) => {
      if (isDemoDataMode) {
        return mapDemoMarketsToOpportunities(demoMarketScanner.slice(0, take), sourcePrefix);
      }

      const response = await getLineShopping(signal, take, false, normalizeLineShoppingReadSport(sport));
      return mapMarketGroupsToOpportunities(response.comparisonGroups, `${sourcePrefix}-${sport.toLowerCase()}`);
    },
    [take, sourcePrefix, sport, refreshVersion],
    MARKET_QUERY_OPTIONS
  );
  const markets = query.data ?? [];

  return { ...query, markets };
}

export function useEntryCandidateData(take = 40) {
  const refreshVersion = useMarketDataRefreshVersion();
  const query = useApiQuery(
    async (signal) => {
      if (isDemoDataMode) {
        return mapDemoMarketsToOpportunities(demoEntryCandidates.slice(0, take), 'entry-candidate');
      }

      const response = await getTopPicks(signal, take);
      return mapMarketGroupsToOpportunities(response.topPicks, 'entry-candidate')
        .filter((market) => market.providerCategory === 'DFS' && market.canAddToEntry);
    },
    [take, refreshVersion],
    MARKET_QUERY_OPTIONS
  );
  const markets = query.data ?? [];

  return { ...query, markets };
}

export function usePerformanceData(gradingMode: PerformanceGradingMode = 'latestGraded') {
  const refreshVersion = useMarketDataRefreshVersion();
  const query = useApiQuery(
    async (signal) => {
      if (isDemoDataMode) {
        return { rows: demoPerformance, summary: undefined };
      }

      const response = await getPerformanceSummary(signal, gradingMode);
      return { rows: mapPerformanceSummaryToGroups(response), summary: response };
    },
    [gradingMode, refreshVersion],
    MARKET_QUERY_OPTIONS
  );
  const rows = useMemo(
    () => query.data?.rows ?? [],
    [query.data]
  );

  return { ...query, rows, summary: query.data?.summary };
}

export function usePerformanceResults(date?: string, enabled = true) {
  const refreshVersion = useMarketDataRefreshVersion();
  return useApiQuery(
    async (signal) => {
      if (isDemoDataMode || !enabled) {
        return undefined;
      }

      return getPerformanceResults(signal, date);
    },
    [date, enabled, refreshVersion],
    MARKET_QUERY_OPTIONS
  );
}

function normalizeLineShoppingReadSport(sport: MarketReadSport) {
  return sport === 'All' ? 'All' : sport;
}

function normalizeTopPicksReadSport(sport: MarketReadSport) {
  return sport === 'All' ? undefined : sport;
}

function mapDemoMarketsToOpportunities(markets: MarketOpportunity[] | undefined, sourcePrefix: string) {
  return (markets ?? []).map((market, index) => ({
    ...market,
    id: `${sourcePrefix}-${market.id}`,
    rank: market.rank ?? index + 1
  }));
}
