import { useMemo } from 'react';
import { getLineShopping, getMarketFreshness, getPerformanceSummary, getProviderStatus, getScheduledRefreshStatus, getTopPicks } from '../api/markets';
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

export function useMarketFreshnessData(deps: readonly unknown[] = []) {
  const refreshVersion = useMarketDataRefreshVersion();
  return useApiQuery((signal) => (isDemoDataMode ? Promise.resolve(demoMarketFreshness) : getMarketFreshness(signal)), [...deps, refreshVersion]);
}

export function useProviderStatusData(deps: readonly unknown[] = []) {
  const refreshVersion = useMarketDataRefreshVersion();
  return useApiQuery((signal) => (isDemoDataMode ? Promise.resolve(demoProviderStatus) : getProviderStatus(signal)), [...deps, refreshVersion]);
}

export function useScheduledRefreshStatusData(deps: readonly unknown[] = []) {
  const refreshVersion = useMarketDataRefreshVersion();
  return useApiQuery((signal) => (isDemoDataMode ? Promise.resolve(demoScheduledRefreshStatus) : getScheduledRefreshStatus(signal)), [...deps, refreshVersion]);
}

export function useTopPicksData(take = 30, sourcePrefix = 'top-pick') {
  const refreshVersion = useMarketDataRefreshVersion();
  const query = useApiQuery(
    async (signal) => {
      if (isDemoDataMode) {
        return mapDemoMarketsToOpportunities(demoTopPicks.slice(0, take), sourcePrefix);
      }

      const response = await getTopPicks(signal, take);
      return mapMarketGroupsToOpportunities(response.topPicks, sourcePrefix);
    },
    [take, sourcePrefix, refreshVersion]
  );
  const markets = query.data ?? [];

  return { ...query, markets };
}

export function useLineShoppingData(take = 50, topOnly = false, sourcePrefix = 'line-shopping') {
  const refreshVersion = useMarketDataRefreshVersion();
  const query = useApiQuery(
    async (signal) => {
      if (isDemoDataMode) {
        return mapDemoMarketsToOpportunities(demoLineShopping.slice(0, take), sourcePrefix);
      }

      const response = await getLineShopping(signal, take, topOnly);
      return mapMarketGroupsToOpportunities(response.comparisonGroups, sourcePrefix);
    },
    [take, topOnly, sourcePrefix, refreshVersion]
  );
  const markets = query.data ?? [];

  return { ...query, markets };
}

export function useMarketScannerData(take = 75, sourcePrefix = 'market') {
  const refreshVersion = useMarketDataRefreshVersion();
  const query = useApiQuery(
    async (signal) => {
      if (isDemoDataMode) {
        return mapDemoMarketsToOpportunities(demoMarketScanner.slice(0, take), sourcePrefix);
      }

      const response = await getLineShopping(signal, take, false);
      return mapMarketGroupsToOpportunities(response.comparisonGroups, sourcePrefix);
    },
    [take, sourcePrefix, refreshVersion]
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
      return mapMarketGroupsToOpportunities(response.topPicks, 'entry-candidate').filter((market) => market.providerCategory === 'DFS' && market.canAddToEntry);
    },
    [take, refreshVersion]
  );
  const markets = query.data ?? [];

  return { ...query, markets };
}

export function usePerformanceData() {
  const refreshVersion = useMarketDataRefreshVersion();
  const query = useApiQuery(
    async (signal) => {
      if (isDemoDataMode) {
        return demoPerformance;
      }

      const response = await getPerformanceSummary(signal);
      return mapPerformanceSummaryToGroups(response);
    },
    [refreshVersion]
  );
  const rows = useMemo(
    () => query.data ?? [],
    [query.data]
  );

  return { ...query, rows };
}

function mapDemoMarketsToOpportunities(markets: MarketOpportunity[] | undefined, sourcePrefix: string) {
  return (markets ?? []).map((market, index) => ({
    ...market,
    id: `${sourcePrefix}-${market.id}`,
    rank: market.rank ?? index + 1
  }));
}
