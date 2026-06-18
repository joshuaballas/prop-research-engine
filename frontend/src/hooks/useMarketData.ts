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

export function useMarketFreshnessData(deps: readonly unknown[] = []) {
  return useApiQuery((signal) => (isDemoDataMode ? Promise.resolve(demoMarketFreshness) : getMarketFreshness(signal)), deps);
}

export function useProviderStatusData(deps: readonly unknown[] = []) {
  return useApiQuery((signal) => (isDemoDataMode ? Promise.resolve(demoProviderStatus) : getProviderStatus(signal)), deps);
}

export function useScheduledRefreshStatusData(deps: readonly unknown[] = []) {
  return useApiQuery((signal) => (isDemoDataMode ? Promise.resolve(demoScheduledRefreshStatus) : getScheduledRefreshStatus(signal)), deps);
}

export function useTopPicksData(take = 30, sourcePrefix = 'top-pick') {
  const query = useApiQuery(
    async (signal) => {
      if (isDemoDataMode) {
        return mapDemoMarketsToOpportunities(demoTopPicks.slice(0, take), sourcePrefix);
      }

      const response = await getTopPicks(signal, take);
      return mapMarketGroupsToOpportunities(response.topPicks, sourcePrefix);
    },
    [take, sourcePrefix]
  );
  const markets = query.data ?? [];

  return { ...query, markets };
}

export function useLineShoppingData(take = 50, topOnly = false, sourcePrefix = 'line-shopping') {
  const query = useApiQuery(
    async (signal) => {
      if (isDemoDataMode) {
        return mapDemoMarketsToOpportunities(demoLineShopping.slice(0, take), sourcePrefix);
      }

      const response = await getLineShopping(signal, take, topOnly);
      return mapMarketGroupsToOpportunities(response.comparisonGroups, sourcePrefix);
    },
    [take, topOnly, sourcePrefix]
  );
  const markets = query.data ?? [];

  return { ...query, markets };
}

export function useMarketScannerData(take = 75, sourcePrefix = 'market') {
  const query = useApiQuery(
    async (signal) => {
      if (isDemoDataMode) {
        return mapDemoMarketsToOpportunities(demoMarketScanner.slice(0, take), sourcePrefix);
      }

      const response = await getLineShopping(signal, take, false);
      return mapMarketGroupsToOpportunities(response.comparisonGroups, sourcePrefix);
    },
    [take, sourcePrefix]
  );
  const markets = query.data ?? [];

  return { ...query, markets };
}

export function useEntryCandidateData(take = 40) {
  const query = useApiQuery(
    async (signal) => {
      if (isDemoDataMode) {
        return mapDemoMarketsToOpportunities(demoEntryCandidates.slice(0, take), 'entry-candidate');
      }

      const response = await getTopPicks(signal, take);
      return mapMarketGroupsToOpportunities(response.topPicks, 'entry-candidate').filter((market) => market.providerCategory === 'DFS' && market.canAddToEntry);
    },
    [take]
  );
  const markets = query.data ?? [];

  return { ...query, markets };
}

export function usePerformanceData() {
  const query = useApiQuery(
    async (signal) => {
      if (isDemoDataMode) {
        return demoPerformance;
      }

      const response = await getPerformanceSummary(signal);
      return mapPerformanceSummaryToGroups(response);
    },
    []
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
