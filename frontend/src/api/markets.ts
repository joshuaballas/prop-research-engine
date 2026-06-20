import { apiGet, buildQuery } from './client';
import type {
  MarketFreshnessDto,
  MarketPerformanceResultsDto,
  MarketPerformanceSummaryDto,
  PropHistoryResponseDto,
  ProviderStatusDto,
  PublicLineShoppingResponseDto,
  PublicTopPicksResponseDto,
  ScheduledRefreshStatusDto
} from './types';

export type PerformanceGradingMode = 'latestGraded' | 'latestCompleted' | 'currentSlate' | 'rollingWindow';

export function getMarketFreshness(signal?: AbortSignal) {
  return apiGet<MarketFreshnessDto>('/api/markets/freshness', signal);
}

export function getProviderStatus(signal?: AbortSignal) {
  return apiGet<ProviderStatusDto>('/api/markets/provider-status', signal);
}

export function getScheduledRefreshStatus(signal?: AbortSignal) {
  return apiGet<ScheduledRefreshStatusDto>('/api/debug/the-odds/mlb/scheduled-refresh/status', signal);
}

export function getTopPicks(signal?: AbortSignal, take = 25) {
  return apiGet<PublicTopPicksResponseDto>(
    `/api/markets/top-picks${buildQuery({ sport: 'MLB', take, providerCategory: 'DFS', freshnessMinutes: 30 })}`,
    signal
  ).then(normalizeTopPicksResponse);
}

export function getLineShopping(signal?: AbortSignal, take = 50, topOnly = true) {
  return apiGet<PublicLineShoppingResponseDto>(
    `/api/markets/line-shopping${buildQuery({
      sport: 'MLB',
      take,
      topOnly,
      freshnessMinutes: 30,
      includeBookmakerPrices: true
    })}`,
    signal
  ).then(normalizeLineShoppingResponse);
}

export function getPerformanceSummary(signal?: AbortSignal, gradingMode: PerformanceGradingMode = 'latestGraded') {
  return apiGet<MarketPerformanceSummaryDto>(
    `/api/markets/performance-summary${buildQuery({ sport: 'MLB', candidateOnly: true, gradingMode })}`,
    signal
  );
}

export function getPerformanceResults(signal?: AbortSignal, date?: string) {
  return apiGet<MarketPerformanceResultsDto>(
    `/api/markets/performance-results${buildQuery({ sport: 'MLB', date })}`,
    signal
  );
}

export interface PropHistoryRequest {
  sport?: string;
  playerId?: string;
  playerName?: string;
  propType: string;
  side: string;
  line: number;
  provider?: string;
  take?: number;
  season?: number;
}

export function getPropHistory(request: PropHistoryRequest, signal?: AbortSignal) {
  return apiGet<PropHistoryResponseDto>(
    `/api/markets/prop-history${buildQuery({
      sport: request.sport ?? 'MLB',
      playerId: request.playerId,
      playerName: request.playerName,
      propType: request.propType,
      side: request.side,
      line: request.line,
      provider: request.provider,
      take: request.take ?? 10,
      season: request.season
    })}`,
    signal
  );
}

function normalizeTopPicksResponse(response: PublicTopPicksResponseDto): PublicTopPicksResponseDto {
  return {
    ...response,
    generatedAtUtc: response.generatedAtUtc ?? response.GeneratedAtUtc ?? '',
    freshness: response.freshness ?? response.Freshness,
    snapshotBacked: response.snapshotBacked ?? response.SnapshotBacked,
    fallbackReason: response.fallbackReason ?? response.FallbackReason,
    summary: response.summary ?? response.Summary,
    topPicks: response.topPicks ?? response.TopPicks ?? []
  };
}

function normalizeLineShoppingResponse(response: PublicLineShoppingResponseDto): PublicLineShoppingResponseDto {
  return {
    ...response,
    generatedAtUtc: response.generatedAtUtc ?? response.GeneratedAtUtc ?? '',
    freshness: response.freshness ?? response.Freshness,
    snapshotBacked: response.snapshotBacked ?? response.SnapshotBacked,
    fallbackReason: response.fallbackReason ?? response.FallbackReason,
    summary: response.summary ?? response.Summary,
    comparisonGroups: response.comparisonGroups ?? response.ComparisonGroups ?? []
  };
}
