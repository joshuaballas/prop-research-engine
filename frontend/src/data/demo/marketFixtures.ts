import type { MarketFreshnessDto, ProviderStatusDto, ScheduledRefreshStatusDto } from '../../api/types';
import { mockMarkets } from '../mockMarkets';
import { mockPerformance } from '../mockPerformance';
import { mockTopPicks } from '../mockTopPicks';

export const demoMarketScanner = mockMarkets;
export const demoLineShopping = mockMarkets.filter((market) => market.providerCategory === 'DFS');
export const demoTopPicks = mockTopPicks;
export const demoEntryCandidates = mockTopPicks.filter((market) => market.providerCategory === 'DFS' && market.canAddToEntry);
export const demoPerformance = mockPerformance;

const generatedAtUtc = new Date(Date.now() - 4 * 60 * 1000).toISOString();

export const demoMarketFreshness: MarketFreshnessDto = {
  generatedAtUtc,
  latestSuccessfulRefresh: {
    id: 'demo-refresh',
    startedAtUtc: generatedAtUtc,
    completedAtUtc: generatedAtUtc,
    succeeded: true,
    dryRun: false,
    forceLive: false,
    bookmakerPreset: 'demo'
  },
  isFresh: true,
  isStale: false,
  staleReason: null,
  hasUsableMarketData: true,
  minutesSinceLatestSuccessfulRefresh: 4,
  lineShoppingSnapshotCount: demoLineShopping.length,
  topPickSnapshotCount: demoTopPicks.length,
  activeGroupCount: demoMarketScanner.length,
  staleOrExpiredGroupCount: 0,
  providerIngestionFailureCount: 0,
  providerIngestionSuccessCount: 1,
  providerRejectedKeyDetected: false,
  usabilityWarning: 'Demo market rows are loaded for offline review.',
  providerStatus: {
    status: 'Demo',
    statusReason: 'Using curated demo fixtures.',
    recommendedAction: 'Switch VITE_DATA_MODE back to api for live market reads.'
  }
};

export const demoProviderStatus: ProviderStatusDto = {
  generatedAtUtc,
  providerStatus: 'Demo',
  status: 'Demo',
  statusReason: 'Using curated demo fixtures.',
  recommendedAction: 'Switch VITE_DATA_MODE back to api for live market reads.',
  requestsRemaining: null,
  requestsUsed: null,
  providerRejectedKeyDetected: false,
  latestRefreshHadUsableMarketData: true
};

export const demoScheduledRefreshStatus: ScheduledRefreshStatusDto = {
  enabled: false,
  intervalMinutes: 15,
  effectiveIntervalMinutes: 15,
  bookmakerPreset: 'demo',
  sport: 'MLB',
  providerCategories: ['DFS'],
  requireForceLive: false,
  allowScheduledLiveRefresh: false,
  willCallExternalApis: false,
  callsExternalApis: false,
  nextRunEstimateUtc: null,
  nextRefreshDueUtc: null,
  nextDataAvailableEstimateUtc: null,
  postRefreshReadBufferSeconds: 30,
  latestSuccessfulRefresh: demoMarketFreshness.latestSuccessfulRefresh,
  latestSuccessfulRefreshCompletedAtUtc: generatedAtUtc,
  status: 'Demo',
  currentPlan: {
    shouldRefresh: false,
    skipReason: 'DemoMode',
    providerAvailable: true,
    providerStatus: 'Demo',
    providerRejectedKeyDetected: false,
    recommendedAction: 'Switch VITE_DATA_MODE back to api for live market reads.'
  }
};
