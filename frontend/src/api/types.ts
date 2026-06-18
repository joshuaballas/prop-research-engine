export interface MarketFreshnessDto {
  generatedAtUtc: string;
  latestSuccessfulRefresh?: MarketRefreshRunSummaryDto | null;
  LatestSuccessfulRefresh?: MarketRefreshRunSummaryDto | null;
  isFresh?: boolean;
  isStale?: boolean;
  staleReason?: string | null;
  hasUsableMarketData?: boolean;
  minutesSinceLatestSuccessfulRefresh?: number | null;
  lineShoppingSnapshotCount?: number;
  topPickSnapshotCount?: number;
  activeGroupCount?: number;
  staleOrExpiredGroupCount?: number;
  providerIngestionFailureCount?: number;
  providerIngestionSuccessCount?: number;
  providerRejectedKeyDetected?: boolean;
  usabilityWarning?: string | null;
  providerStatus?: {
    status?: string;
    statusReason?: string;
    recommendedAction?: string;
  };
}

export interface MarketRefreshRunSummaryDto {
  id?: string;
  Id?: string;
  startedAtUtc?: string | null;
  StartedAtUtc?: string | null;
  completedAtUtc?: string | null;
  CompletedAtUtc?: string | null;
  succeeded?: boolean;
  Succeeded?: boolean;
  failed?: boolean;
  Failed?: boolean;
  blocked?: boolean;
  Blocked?: boolean;
  dryRun?: boolean;
  DryRun?: boolean;
  forceLive?: boolean;
  ForceLive?: boolean;
  bookmakerPreset?: string | null;
  BookmakerPreset?: string | null;
}

export interface ProviderStatusDto {
  generatedAtUtc: string;
  providerStatus?: string;
  status?: string;
  statusReason?: string;
  recommendedAction?: string;
  requestsRemaining?: number | null;
  requestsUsed?: number | null;
  providerRejectedKeyDetected?: boolean;
  latestRefreshHadUsableMarketData?: boolean;
}

export interface ScheduledRefreshStatusDto {
  enabled?: boolean;
  Enabled?: boolean;
  intervalMinutes?: number;
  IntervalMinutes?: number;
  effectiveIntervalMinutes?: number;
  EffectiveIntervalMinutes?: number;
  bookmakerPreset?: string;
  BookmakerPreset?: string;
  sport?: string;
  Sport?: string;
  providerCategories?: string[];
  ProviderCategories?: string[];
  requireForceLive?: boolean;
  RequireForceLive?: boolean;
  allowScheduledLiveRefresh?: boolean;
  AllowScheduledLiveRefresh?: boolean;
  nextRunEstimateUtc?: string | null;
  NextRunEstimateUtc?: string | null;
  lastRunStartedAtUtc?: string | null;
  LastRunStartedAtUtc?: string | null;
  lastRunCompletedAtUtc?: string | null;
  LastRunCompletedAtUtc?: string | null;
  lastRunSucceeded?: boolean | null;
  LastRunSucceeded?: boolean | null;
  lastRunBlocked?: boolean | null;
  LastRunBlocked?: boolean | null;
  lastRunError?: string | null;
  LastRunError?: string | null;
  latestSuccessfulRefresh?: MarketRefreshRunSummaryDto | null;
  LatestSuccessfulRefresh?: MarketRefreshRunSummaryDto | null;
  latestSuccessfulRefreshCompletedAtUtc?: string | null;
  LatestSuccessfulRefreshCompletedAtUtc?: string | null;
  callsExternalApis?: boolean;
  CallsExternalApis?: boolean;
  willCallExternalApis?: boolean;
  WillCallExternalApis?: boolean;
  status?: string;
  Status?: string;
  currentPlan?: {
    providerAvailable?: boolean;
    ProviderAvailable?: boolean;
    providerStatus?: string | null;
    ProviderStatus?: string | null;
    providerRejectedKeyDetected?: boolean;
    ProviderRejectedKeyDetected?: boolean;
    recommendedAction?: string | null;
    RecommendedAction?: string | null;
  };
  CurrentPlan?: ScheduledRefreshStatusDto['currentPlan'];
}

export interface PublicTopPicksResponseDto {
  generatedAtUtc: string;
  GeneratedAtUtc?: string;
  freshness?: MarketFreshnessDto;
  Freshness?: MarketFreshnessDto;
  snapshotBacked?: boolean;
  SnapshotBacked?: boolean;
  fallbackReason?: string | null;
  FallbackReason?: string | null;
  summary?: {
    activeCandidateCount?: number;
    ActiveCandidateCount?: number;
    actionableCandidateCount?: number;
    ActionableCandidateCount?: number;
    returnedTopPicks?: number;
    ReturnedTopPicks?: number;
    totalActiveComparisonGroups?: number;
    TotalActiveComparisonGroups?: number;
  };
  Summary?: PublicTopPicksResponseDto['summary'];
  topPicks?: MarketGroupDto[];
  TopPicks?: MarketGroupDto[];
}

export interface PublicLineShoppingResponseDto {
  generatedAtUtc: string;
  GeneratedAtUtc?: string;
  freshness?: MarketFreshnessDto;
  Freshness?: MarketFreshnessDto;
  snapshotBacked?: boolean;
  SnapshotBacked?: boolean;
  fallbackReason?: string | null;
  FallbackReason?: string | null;
  summary?: {
    totalComparisonGroups?: number;
    TotalComparisonGroups?: number;
    totalActiveComparisonGroups?: number;
    TotalActiveComparisonGroups?: number;
  };
  Summary?: PublicLineShoppingResponseDto['summary'];
  comparisonGroups?: MarketGroupDto[];
  ComparisonGroups?: MarketGroupDto[];
}

export interface MarketGroupDto {
  Sport?: string;
  sport?: string;
  eventId?: string | null;
  EventId?: string | null;
  eventStartTime?: string | null;
  EventStartTime?: string | null;
  eventStartTimeUtc?: string | null;
  EventStartTimeUtc?: string | null;
  lastObservedAt?: string;
  LastObservedAt?: string;
  isFresh?: boolean;
  IsFresh?: boolean;
  isStale?: boolean;
  IsStale?: boolean;
  isExpired?: boolean;
  IsExpired?: boolean;
  lifecycleStatus?: string;
  LifecycleStatus?: string;
  lifecycleReasons?: string[];
  LifecycleReasons?: string[];
  homeTeam?: string | null;
  HomeTeam?: string | null;
  awayTeam?: string | null;
  AwayTeam?: string | null;
  player?: string;
  Player?: string;
  normalizedPropType?: string;
  NormalizedPropType?: string;
  line?: number;
  Line?: number;
  marketClassification?: string;
  MarketClassification?: string;
  providerCategory?: string;
  ProviderCategory?: string;
  isActionableForUser?: boolean;
  IsActionableForUser?: boolean;
  historicalSampleSize?: number;
  HistoricalSampleSize?: number;
  sampleWarning?: string;
  SampleWarning?: string;
  trustLevel?: string;
  TrustLevel?: string;
  confidenceScore?: number | null;
  ConfidenceScore?: number | null;
  bookmakerPrices?: BookmakerPriceDto[];
  BookmakerPrices?: BookmakerPriceDto[];
  bestSide?: string;
  BestSide?: string;
  bestBookmaker?: string | null;
  BestBookmaker?: string | null;
  bestAmericanPrice?: number | null;
  BestAmericanPrice?: number | null;
  bestDecimalPrice?: number | null;
  BestDecimalPrice?: number | null;
  bestProbability?: number | null;
  BestProbability?: number | null;
  bestImpliedProbability?: number | null;
  BestImpliedProbability?: number | null;
  bestProbabilityEdge?: number | null;
  BestProbabilityEdge?: number | null;
  bestEstimatedEv?: number | null;
  BestEstimatedEv?: number | null;
  strategyClassification?: string;
  StrategyClassification?: string;
  strategyTier?: string;
  StrategyTier?: string;
  strategyReasons?: string[];
  StrategyReasons?: string[];
  strategyWarnings?: string[];
  StrategyWarnings?: string[];
  isCandidate?: boolean;
  IsCandidate?: boolean;
  isAvoid?: boolean;
  IsAvoid?: boolean;
  isWatchlist?: boolean;
  IsWatchlist?: boolean;
  isResearchOnly?: boolean;
  IsResearchOnly?: boolean;
  marketShape?: string;
  MarketShape?: string;
  riskTags?: string[];
  RiskTags?: string[];
  entryCompatibilityGroup?: string;
  EntryCompatibilityGroup?: string;
  entryProvider?: string;
  EntryProvider?: string;
  entryProviderCategory?: string;
  EntryProviderCategory?: string;
  entryActionableForUser?: boolean;
  EntryActionableForUser?: boolean;
  entryRuleWarning?: string | null;
  EntryRuleWarning?: string | null;
  supportedEntryTypes?: string[];
  SupportedEntryTypes?: string[];
  allowedLegCounts?: number[];
  AllowedLegCounts?: number[];
}

export interface BookmakerPriceDto {
  bookmaker?: string;
  Bookmaker?: string;
  bookmakerKey?: string;
  BookmakerKey?: string;
  providerCategory?: string;
  ProviderCategory?: string;
  line?: number | null;
  Line?: number | null;
  overPrice?: number | null;
  OverPrice?: number | null;
  underPrice?: number | null;
  UnderPrice?: number | null;
  region?: string;
  Region?: string;
}

export interface MarketPerformanceSummaryDto {
  generatedAtUtc: string;
  dataAvailability?: {
    snapshotRowsLoaded?: number;
    picksAfterFilters?: number;
    gradedPicks?: number;
    hasEnoughData?: boolean;
    warning?: string | null;
  };
  groups?: {
    overall?: PerformanceGroupDto[];
    byProviderCategory?: PerformanceGroupDto[];
    byProvider?: PerformanceGroupDto[];
    byBookmaker?: PerformanceGroupDto[];
    byPropType?: PerformanceGroupDto[];
    byStrategyClassification?: PerformanceGroupDto[];
    byStrategyTier?: PerformanceGroupDto[];
    byProviderAndPropType?: PerformanceGroupDto[];
  };
}

export interface PerformanceGroupDto {
  groupType: string;
  groupKey: string;
  sport?: string;
  gradedPicks?: number;
  wins?: number;
  losses?: number;
  pushes?: number;
  hitRate?: number;
  averageEstimatedEv?: number | null;
  simulatedRoi?: number;
  sampleSizeWarning?: string | null;
  notes?: string[];
}
