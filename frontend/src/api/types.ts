export interface MarketFreshnessDto {
  generatedAtUtc: string;
  latestSuccessfulRefresh?: MarketRefreshRunSummaryDto | null;
  LatestSuccessfulRefresh?: MarketRefreshRunSummaryDto | null;
  isFresh?: boolean;
  isStale?: boolean;
  staleReason?: string | null;
  hasUsableMarketData?: boolean;
  minutesSinceLatestSuccessfulRefresh?: number | null;
  lastSuccessfulRefreshCompletedAtUtc?: string | null;
  latestSuccessfulRefreshCompletedAtUtc?: string | null;
  nextRefreshAtUtc?: string | null;
  nextRefreshDueUtc?: string | null;
  nextRefreshStartEstimateUtc?: string | null;
  nextHeaderSyncTargetUtc?: string | null;
  nextDataAvailableEstimateUtc?: string | null;
  secondsUntilNextRefresh?: number | null;
  secondsUntilNextRefreshDue?: number | null;
  secondsUntilNextDataAvailable?: number | null;
  isRefreshInProgress?: boolean | null;
  isOverdue?: boolean | null;
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
  generatedAtUtc?: string;
  GeneratedAtUtc?: string;
  nowUtc?: string;
  NowUtc?: string;
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
  workerRegistered?: boolean;
  WorkerRegistered?: boolean;
  workerRunning?: boolean;
  WorkerRunning?: boolean;
  lastWakeAtUtc?: string | null;
  LastWakeAtUtc?: string | null;
  nextWakeEstimateUtc?: string | null;
  NextWakeEstimateUtc?: string | null;
  workerNextWakeUtc?: string | null;
  WorkerNextWakeUtc?: string | null;
  nextRunEstimateUtc?: string | null;
  NextRunEstimateUtc?: string | null;
  nextRefreshAtUtc?: string | null;
  NextRefreshAtUtc?: string | null;
  nextRefreshDueUtc?: string | null;
  NextRefreshDueUtc?: string | null;
  nextRefreshStartEstimateUtc?: string | null;
  NextRefreshStartEstimateUtc?: string | null;
  nextHeaderSyncTargetUtc?: string | null;
  NextHeaderSyncTargetUtc?: string | null;
  nextDataAvailableEstimateUtc?: string | null;
  NextDataAvailableEstimateUtc?: string | null;
  conservativeDataAvailableEstimateUtc?: string | null;
  ConservativeDataAvailableEstimateUtc?: string | null;
  secondsUntilNextRefreshStart?: number | null;
  SecondsUntilNextRefreshStart?: number | null;
  secondsUntilNextRefresh?: number | null;
  SecondsUntilNextRefresh?: number | null;
  secondsUntilNextRefreshDue?: number | null;
  SecondsUntilNextRefreshDue?: number | null;
  secondsUntilNextDataAvailable?: number | null;
  SecondsUntilNextDataAvailable?: number | null;
  secondsUntilConservativeDataAvailable?: number | null;
  SecondsUntilConservativeDataAvailable?: number | null;
  expectedRefreshDurationSeconds?: number;
  ExpectedRefreshDurationSeconds?: number;
  postRefreshReadBufferSeconds?: number;
  PostRefreshReadBufferSeconds?: number;
  isRefreshInProgress?: boolean;
  IsRefreshInProgress?: boolean;
  isOverdue?: boolean;
  IsOverdue?: boolean;
  estimateBasis?: string | null;
  EstimateBasis?: string | null;
  latestRefreshRunId?: string | null;
  LatestRefreshRunId?: string | null;
  latestSuccessfulRefreshCompletedAtUtc?: string | null;
  LatestSuccessfulRefreshCompletedAtUtc?: string | null;
  lastSuccessfulRefreshCompletedAtUtc?: string | null;
  LastSuccessfulRefreshCompletedAtUtc?: string | null;
  currentRunStartedAtUtc?: string | null;
  CurrentRunStartedAtUtc?: string | null;
  currentRunScheduledDueUtc?: string | null;
  CurrentRunScheduledDueUtc?: string | null;
  currentRunElapsedSeconds?: number | null;
  CurrentRunElapsedSeconds?: number | null;
  currentRunEstimatedRemainingSeconds?: number | null;
  CurrentRunEstimatedRemainingSeconds?: number | null;
  activeRunDataAvailableTargetUtc?: string | null;
  ActiveRunDataAvailableTargetUtc?: string | null;
  nextCycleRefreshDueUtc?: string | null;
  NextCycleRefreshDueUtc?: string | null;
  nextCycleDataAvailableEstimateUtc?: string | null;
  NextCycleDataAvailableEstimateUtc?: string | null;
  dataAvailableStatus?: string | null;
  DataAvailableStatus?: string | null;
  lastRunScheduledDueUtc?: string | null;
  LastRunScheduledDueUtc?: string | null;
  lastRunStartedDelaySeconds?: number | null;
  LastRunStartedDelaySeconds?: number | null;
  lastRunDurationSeconds?: number | null;
  LastRunDurationSeconds?: number | null;
  lastRunPhaseTimings?: unknown | null;
  LastRunPhaseTimings?: unknown | null;
  lastRunStartedAtUtc?: string | null;
  LastRunStartedAtUtc?: string | null;
  lastRunCompletedAtUtc?: string | null;
  LastRunCompletedAtUtc?: string | null;
  lastRunSucceeded?: boolean | null;
  LastRunSucceeded?: boolean | null;
  lastRunSkipped?: boolean | null;
  LastRunSkipped?: boolean | null;
  lastRunBlocked?: boolean | null;
  LastRunBlocked?: boolean | null;
  lastRunError?: string | null;
  LastRunError?: string | null;
  lastPlanShouldRefresh?: boolean | null;
  LastPlanShouldRefresh?: boolean | null;
  lastPlanSkipReason?: string | null;
  LastPlanSkipReason?: string | null;
  lastSkipReason?: string | null;
  LastSkipReason?: string | null;
  latestSuccessfulRefresh?: MarketRefreshRunSummaryDto | null;
  LatestSuccessfulRefresh?: MarketRefreshRunSummaryDto | null;
  latestRun?: MarketRefreshRunSummaryDto | null;
  LatestRun?: MarketRefreshRunSummaryDto | null;
  latestSuccessfulRefreshId?: string | null;
  LatestSuccessfulRefreshId?: string | null;
  latestSuccessfulRefreshRunId?: string | null;
  LatestSuccessfulRefreshRunId?: string | null;
  actualApiCalls?: number;
  ActualApiCalls?: number;
  refreshedTargets?: unknown[];
  RefreshedTargets?: unknown[];
  skippedTargets?: unknown[];
  SkippedTargets?: unknown[];
  callsExternalApis?: boolean;
  CallsExternalApis?: boolean;
  willCallExternalApis?: boolean;
  WillCallExternalApis?: boolean;
  executionBlockedReason?: string | null;
  ExecutionBlockedReason?: string | null;
  status?: string;
  Status?: string;
  currentPlan?: {
    generatedAtUtc?: string | null;
    GeneratedAtUtc?: string | null;
    shouldRefresh?: boolean;
    ShouldRefresh?: boolean;
    skipReason?: string | null;
    SkipReason?: string | null;
    estimatedApiCalls?: number;
    EstimatedApiCalls?: number;
    estimateSource?: string | null;
    EstimateSource?: string | null;
    budgetAllowed?: boolean;
    BudgetAllowed?: boolean;
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
  playerId?: string | null;
  PlayerId?: string | null;
  canonicalPlayerId?: string | null;
  CanonicalPlayerId?: string | null;
  player?: string;
  Player?: string;
  normalizedPropType?: string;
  NormalizedPropType?: string;
  line?: number;
  Line?: number;
  marketClassification?: string;
  MarketClassification?: string;
  marketKey?: string | null;
  MarketKey?: string | null;
  projectionType?: string;
  ProjectionType?: string;
  projectionTypeConfidence?: string;
  ProjectionTypeConfidence?: string;
  isAlternateMarket?: boolean;
  IsAlternateMarket?: boolean;
  payoutMultiplierMayDiffer?: boolean;
  PayoutMultiplierMayDiffer?: boolean;
  specialProjectionWarning?: string | null;
  SpecialProjectionWarning?: string | null;
  providerCategory?: string;
  ProviderCategory?: string;
  competition?: string | null;
  Competition?: string | null;
  isDfs?: boolean;
  IsDfs?: boolean;
  isSportsbook?: boolean;
  IsSportsbook?: boolean;
  showExpectedValue?: boolean;
  ShowExpectedValue?: boolean;
  displayExpectedValue?: number | null;
  DisplayExpectedValue?: number | null;
  expectedValueUnavailableReason?: string | null;
  ExpectedValueUnavailableReason?: string | null;
  rawExpectedValue?: number | null;
  RawExpectedValue?: number | null;
  calibratedExpectedValue?: number | null;
  CalibratedExpectedValue?: number | null;
  evCalibrationMethod?: string | null;
  EvCalibrationMethod?: string | null;
  evSuppressedReason?: string | null;
  EvSuppressedReason?: string | null;
  sportsbookContextResearchOnly?: boolean;
  SportsbookContextResearchOnly?: boolean;
  isRecommendation?: boolean;
  recommendationActionableForUser?: boolean;
  recommendationType?: string | null;
  resultGradingSupported?: boolean;
  ResultGradingSupported?: boolean;
  resultGradingUnavailableReason?: string | null;
  ResultGradingUnavailableReason?: string | null;
  entryUnavailableReason?: string | null;
  EntryUnavailableReason?: string | null;
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
  hitChance?: number | null;
  rawWeightedProbability?: number | null;
  smoothedProbability?: number | null;
  recentModifier?: number | null;
  marketCalibrationAdjustment?: number | null;
  uncertaintyPenalty?: number | null;
  effectiveSampleSize?: number | null;
  reliabilityScore?: number | null;
  topPickScore?: number | null;
  payoutQualityScore?: number | null;
  priceGateStatus?: string | null;
  priceGateReason?: string | null;
  playablePriceEligible?: boolean | null;
  topPickQualityEligible?: boolean | null;
  lineUtilityScore?: number | null;
  effectiveAmericanOdds?: number | null;
  alternateLineGroupKey?: string | null;
  selectedRecommendedLine?: boolean | null;
  modelVersion?: string | null;
  probabilityModelVersion?: string | null;
  recommendationContractVersion?: string | null;
  fatalConflictCodes?: string[];
  rawSelectedSideProbability?: number | null;
  RawSelectedSideProbability?: number | null;
  calibratedSelectedSideProbability?: number | null;
  CalibratedSelectedSideProbability?: number | null;
  probabilityCalibrationMethod?: string | null;
  ProbabilityCalibrationMethod?: string | null;
  probabilityCalibrationReason?: string | null;
  ProbabilityCalibrationReason?: string | null;
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
  playerHistorySummary?: PlayerHistorySummaryDto | null;
  PlayerHistorySummary?: PlayerHistorySummaryDto | null;
  supportedEntryTypes?: string[];
  SupportedEntryTypes?: string[];
  allowedLegCounts?: number[];
  AllowedLegCounts?: number[];
}

export interface PlayerHistorySummaryDto {
  available?: boolean;
  Available?: boolean;
  unavailableReason?: string | null;
  UnavailableReason?: string | null;
  provider?: string;
  Provider?: string;
  providerPlayerId?: string | null;
  ProviderPlayerId?: string | null;
  sampleSize?: number;
  SampleSize?: number;
  latestResultDate?: string | null;
  LatestResultDate?: string | null;
  stats?: Record<string, PlayerHistoryStatSummaryDto | undefined>;
  Stats?: Record<string, PlayerHistoryStatSummaryDto | undefined>;
  competitionBreakdown?: PlayerHistoryCompetitionBreakdownDto[];
  CompetitionBreakdown?: PlayerHistoryCompetitionBreakdownDto[];
  recentGameLog?: PlayerHistoryGameLogRowDto[];
  RecentGameLog?: PlayerHistoryGameLogRowDto[];
}

export interface PlayerHistoryStatSummaryDto {
  count?: number;
  Count?: number;
  average?: number;
  Average?: number;
  last5Average?: number;
  Last5Average?: number;
  last10Average?: number;
  Last10Average?: number;
}

export interface PlayerHistoryCompetitionBreakdownDto {
  competitionKey?: string | null;
  CompetitionKey?: string | null;
  competitionName?: string | null;
  CompetitionName?: string | null;
  sampleSize?: number;
  SampleSize?: number;
  latestResultDate?: string | null;
  LatestResultDate?: string | null;
}

export interface PlayerHistoryGameLogRowDto {
  eventDate?: string | null;
  EventDate?: string | null;
  competition?: string | null;
  Competition?: string | null;
  competitionKey?: string | null;
  CompetitionKey?: string | null;
  fixtureId?: string | null;
  FixtureId?: string | null;
  stats?: Record<string, number | undefined>;
  Stats?: Record<string, number | undefined>;
}

export interface BookmakerPriceDto {
  bookmaker?: string;
  Bookmaker?: string;
  bookmakerKey?: string;
  BookmakerKey?: string;
  providerCategory?: string;
  ProviderCategory?: string;
  marketKey?: string | null;
  MarketKey?: string | null;
  projectionType?: string;
  ProjectionType?: string;
  projectionTypeConfidence?: string;
  ProjectionTypeConfidence?: string;
  isAlternateMarket?: boolean;
  IsAlternateMarket?: boolean;
  payoutMultiplierMayDiffer?: boolean;
  PayoutMultiplierMayDiffer?: boolean;
  specialProjectionWarning?: string | null;
  SpecialProjectionWarning?: string | null;
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
  gradingMode?: 'allHistory' | 'latestGraded' | 'latestCompleted' | 'currentSlate' | 'rollingWindow' | string;
  primaryGrain?: string;
  opportunityCount?: number;
  providerLineCount?: number;
  dedupedProviderLineCount?: number;
  providerLineDiagnostics?: ProviderLineDiagnosticsDto;
  performanceGrainDiagnostics?: PerformanceGrainDiagnosticsDto;
  yesterday?: PerformancePeriodDiagnosticsDto;
  overall?: PerformancePeriodDiagnosticsDto;
  dataAvailability?: {
    primaryGrain?: string;
    opportunityCount?: number;
    providerLineCount?: number;
    dedupedProviderLineCount?: number;
    gradeableOpportunityCount?: number;
    gradedOpportunityCount?: number;
    noResultOpportunityCount?: number;
    providerLineDiagnostics?: ProviderLineDiagnosticsDto;
    performanceGrainDiagnostics?: PerformanceGrainDiagnosticsDto;
    yesterdayDate?: string | null;
    yesterdayGradedOpportunities?: number;
    overallGradedOpportunities?: number;
    overallIncludesLatestGradedSlate?: boolean;
    overallDateRange?: PerformanceDateRangeDto | null;
    overallExcludedLatestGradedSlateReason?: string | null;
    snapshotRowsLoaded?: number;
    picksAfterFilters?: number;
    totalPicksAfterFilters?: number;
    gradedPicks?: number;
    hasEnoughData?: boolean;
    warning?: string | null;
    latestGradedBlocker?: string | null;
    latestGradedBlockerDetail?: string | null;
    latestSnapshotDate?: string | null;
    latestGradedDate?: string | null;
    latestCompletedDate?: string | null;
    latestResultsImportDate?: string | null;
    currentSlateDate?: string | null;
    currentSlateTotalPicks?: number;
    currentSlateGradedPicks?: number;
    currentSlatePendingPicks?: number;
    snapshotRowsForCompletedDate?: number;
    ledgerRowsForCompletedDate?: number;
    candidateLedgerRowsForCompletedDate?: number;
    filteredRowsForCompletedDate?: number;
    gradedRowsForCompletedDate?: number;
    noResultRowsForCompletedDate?: number;
    unmatchedRowsForCompletedDate?: number;
    pendingRowsForCompletedDate?: number;
    wonRowsForCompletedDate?: number;
    lostRowsForCompletedDate?: number;
    pushedRowsForCompletedDate?: number;
    importedResultsForCompletedDate?: number;
    propResultsForCompletedDate?: number;
    noResultPicks?: number;
    hasGradeableSample?: boolean;
    gradeableGroupCount?: number;
    noResultOnlyGroupCount?: number;
    selectedGradedDate?: string | null;
    selectedGradedDateCoverageStatus?: string | null;
    unmatchedPicks?: number;
    hasLedgerRowsForCompletedDate?: boolean;
    hasFilteredRowsForCompletedDate?: boolean;
    hasGradedData?: boolean;
    hasPendingCurrentSlate?: boolean;
    pendingReason?: string | null;
    usedDateRange?: {
      fromDate?: string | null;
      toDate?: string | null;
    } | null;
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
  diagnostics?: {
    primaryGrain?: string;
    providerLineDiagnostics?: ProviderLineDiagnosticsDto;
    performanceGrainDiagnostics?: PerformanceGrainDiagnosticsDto;
    yesterday?: PerformancePeriodDiagnosticsDto;
    overall?: PerformancePeriodDiagnosticsDto;
    [key: string]: unknown;
  } | null;
}

export interface MarketPerformanceResultsDto {
  generatedAtUtc?: string;
  callsExternalApis?: boolean;
  readOnly?: boolean;
  requestedSport?: string | null;
  sport?: string;
  primaryGrain?: string;
  opportunityCount?: number;
  providerLineCount?: number;
  dedupedProviderLineCount?: number;
  providerLineDiagnostics?: ProviderLineDiagnosticsDto;
  date?: string;
  summary?: {
    gradedPicks?: number;
    gradedOpportunities?: number;
    opportunityCount?: number;
    providerLineCount?: number;
    dedupedProviderLineCount?: number;
    won?: number;
    lost?: number;
    pushed?: number;
    noResultExcluded?: number;
    hitRate?: number | null;
    simulatedRoi?: number | null;
    avgEvAtRecommendationTime?: number | null;
  };
  rows?: PerformanceResultRowDto[];
  diagnostics?: {
    primaryGrain?: string;
    providerLineDiagnostics?: ProviderLineDiagnosticsDto;
    noResultCounts?: Record<string, number>;
    noResultExamples?: unknown[];
    matchMethodCounts?: Record<string, number>;
  } | null;
}

export interface ProviderLineDiagnosticsDto {
  primaryGrain?: string;
  providerLineGrain?: string;
  providerLineCount?: number;
  dedupedProviderLineCount?: number;
  opportunityCount?: number;
  gradeableOpportunityCount?: number;
  gradedOpportunityCount?: number;
  noResultOpportunityCount?: number;
  duplicateProviderLineCount?: number;
  sportsbookOpportunityKeyExcludesBookmaker?: boolean;
  dfsOpportunityKeyIncludesEntryProvider?: boolean;
}

export interface PerformanceDateRangeDto {
  fromDate?: string | null;
  toDate?: string | null;
}

export interface PerformancePeriodDiagnosticsDto {
  date?: string | null;
  wlp?: string;
  won?: number;
  lost?: number;
  pushed?: number;
  gradedOpportunities?: number;
  includesLatestGradedSlate?: boolean;
  dateRange?: PerformanceDateRangeDto | null;
  excludedLatestGradedSlateReason?: string | null;
  providerLineDiagnostics?: ProviderLineDiagnosticsDto;
}

export interface PerformanceGrainDiagnosticsDto {
  primaryGrain?: string;
  latestGradedDate?: string | null;
  yesterdayDate?: string | null;
  yesterdayGradedOpportunities?: number;
  overallGradedOpportunities?: number;
  overallIncludesLatestGradedSlate?: boolean;
  overallDateRange?: PerformanceDateRangeDto | null;
  overallExcludedLatestGradedSlateReason?: string | null;
  modelEra?: string | null;
  includeLegacy?: boolean;
  excludedOpportunityCount?: number;
  providerLineDiagnostics?: ProviderLineDiagnosticsDto;
}

export interface PerformanceResultRowDto {
  recommendationSnapshotId: string;
  result: 'Won' | 'Lost' | 'Pushed' | string;
  playerName: string;
  propType: string;
  side: 'Over' | 'Under' | string;
  line: number;
  actualValue: number | null;
  provider: string;
  bookmaker?: string | null;
  providerCategory?: string | null;
  isDfs?: boolean;
  isSportsbook?: boolean;
  showExpectedValue?: boolean;
  displayExpectedValue?: number | null;
  expectedValueUnavailableReason?: string | null;
  eventStartTimeUtc?: string | null;
  estimatedEv?: number | null;
  hitChance?: number | null;
  trust?: string | null;
  sampleSize?: number | null;
  matchMethod?: string | null;
  providerLineCount?: number;
  providerLineProviders?: string[];
  providerLineBookmakers?: string[];
}

export interface PerformanceGroupDto {
  groupType: string;
  groupKey: string;
  sport?: string;
  gradedPicks?: number;
  wins?: number;
  losses?: number;
  pushes?: number;
  noResults?: number;
  unmatched?: number;
  pending?: number;
  hasGradeableSample?: boolean;
  isNoResultOnly?: boolean;
  hitRate?: number;
  averageEstimatedEv?: number | null;
  simulatedRoi?: number;
  sampleSizeWarning?: string | null;
  notes?: string[];
}

export interface PropHistoryResponseDto {
  sport?: string;
  playerId?: string | null;
  player?: string | null;
  propType?: string | null;
  displayPropType?: string;
  side?: 'Over' | 'Under' | string;
  line?: number | null;
  provider?: string | null;
  season?: number | null;
  games?: PropHistoryGameDto[];
  summary?: PropHistorySummaryDto;
  warning?: string | null;
  emptyReason?: string | null;
  diagnostics?: PropHistoryDiagnosticsDto;
}

export interface PropHistoryGameDto {
  date?: string;
  dateLabel?: string;
  event?: string;
  gameLabel?: string;
  value?: number;
  line?: number;
  selectedLine?: number;
  side?: 'Over' | 'Under' | string;
  clearedSelectedSide?: boolean | null;
  result?: 'Cleared' | 'Missed' | 'Push' | 'cleared' | 'missed' | 'pushed' | string;
  externalSourceIdentifier?: string;
}

export interface PropHistorySummaryDto {
  sampleSize?: number;
  gradedResults?: number;
  cleared?: number;
  missed?: number;
  pushed?: number;
  hitRate?: number | null;
  last5Average?: number | null;
  last10Average?: number | null;
  seasonAverage?: number | null;
  bestValue?: number | null;
  bestLabel?: string | null;
  bestRecentResult?: string | null;
}

export interface PropHistoryDiagnosticsDto {
  lookupMode?: string;
  playerResolved?: boolean;
  resolvedPlayerId?: string | null;
  playerNameFallbackUsed?: boolean;
  warning?: string | null;
  propTypeResolved?: string | null;
  historySource?: string;
  equivalentPlayerCount?: number;
  candidateObservationCount?: number;
  observationCountAfterPropType?: number;
  observationCountAfterSeason?: number;
}

export interface PlayerSearchResultDto {
  playerId: string;
  effectivePlayerId?: string | null;
  displayName: string;
  league?: string | null;
  sport?: string | null;
  team?: string | null;
  position?: string | null;
  subtitle?: string | null;
}

export interface PlayerResearchCurrentLinesResponseDto {
  generatedAtUtc: string;
  callsExternalApis?: boolean;
  readOnly?: boolean;
  playerId?: string | null;
  effectivePlayerId?: string | null;
  lines?: MarketGroupDto[];
  audit?: unknown;
}
