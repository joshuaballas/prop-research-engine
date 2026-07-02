import type { MarketOpportunity, PlayerHistoryStatKey, PlayerHistorySummary, ProjectionModifier, ProviderCategory, StrategyClassification } from '../types/markets';
import type { PerformanceGroup } from '../types/performance';
import { formatAmericanOdds } from '../utils/formatters';
import type { BookmakerPriceDto, MarketGroupDto, MarketPerformanceSummaryDto, PerformanceGroupDto, PlayerHistoryCompetitionBreakdownDto, PlayerHistoryGameLogRowDto, PlayerHistoryStatSummaryDto, PlayerHistorySummaryDto, TopPickProviderOptionDto } from './types';

export function mapMarketGroupsToOpportunities(groups: MarketGroupDto[] | undefined, sourcePrefix = 'market'): MarketOpportunity[] {
  return (groups ?? []).map((group, index) => mapMarketGroupToOpportunity(group, index, sourcePrefix));
}

export function mapPerformanceSummaryToGroups(summary: MarketPerformanceSummaryDto | undefined): PerformanceGroup[] {
  if (!summary?.groups) {
    return [];
  }

  return [
    ...(summary.groups.overall ?? []).map((row) => mapPerformanceGroup(row, 'Overall')),
    ...(summary.groups.byProvider ?? []).map((row) => mapPerformanceGroup(row, 'Provider')),
    ...(summary.groups.byProviderCategory ?? []).map((row) => mapPerformanceGroup(row, 'Provider')),
    ...(summary.groups.byPropType ?? []).map((row) => mapPerformanceGroup(row, 'Prop Type')),
    ...(summary.groups.byStrategyClassification ?? []).map((row) => mapPerformanceGroup(row, 'Model Segment')),
    ...(summary.groups.byStrategyTier ?? []).map((row) => mapPerformanceGroup(row, 'Model Segment')),
    ...(summary.groups.byProviderAndPropType ?? []).map((row) => mapPerformanceGroup(row, 'Provider + Prop Type'))
  ];
}

function mapMarketGroupToOpportunity(group: MarketGroupDto, index: number, sourcePrefix: string): MarketOpportunity {
  const side = normalizeSide(readString(group, 'bestSide', 'BestSide'));
  const entryActionable = readBoolean(group, 'entryActionableForUser', 'EntryActionableForUser') ?? readBoolean(group, 'isActionableForUser', 'IsActionableForUser') ?? false;
  const providerCategory = normalizeProviderCategory(
    readString(group, 'entryProviderCategory', 'EntryProviderCategory') ?? readString(group, 'providerCategory', 'ProviderCategory'),
    entryActionable
  );
  const isDfs = readBoolean(group, 'isDfs', 'IsDfs') ?? providerCategory === 'DFS';
  const isSportsbook = readBoolean(group, 'isSportsbook', 'IsSportsbook') ?? providerCategory === 'Sportsbook';
  const rawEstimatedEv = readNumber(group, 'bestEstimatedEv', 'BestEstimatedEv') ?? 0;
  const rawExpectedValue = readNumber(group, 'rawExpectedValue', 'RawExpectedValue') ?? null;
  const calibratedExpectedValue = readNumber(group, 'calibratedExpectedValue', 'CalibratedExpectedValue') ?? null;
  const evCalibrationMethod = readString(group, 'evCalibrationMethod', 'EvCalibrationMethod') ?? null;
  const evSuppressedReason = readString(group, 'evSuppressedReason', 'EvSuppressedReason') ?? null;
  const selectedSideProbability = readNumber(group, 'selectedSideProbability', 'SelectedSideProbability', 'bestProbability', 'BestProbability') ?? null;
  const hitChance = readNumber(group, 'hitChance') ?? selectedSideProbability;
  const topPickScore = readNumber(group, 'topPickScore') ?? null;
  const payoutQualityScore = readNumber(group, 'payoutQualityScore') ?? null;
  const priceGateStatus = readString(group, 'priceGateStatus') ?? null;
  const priceGateReason = readString(group, 'priceGateReason') ?? null;
  const playablePriceEligible = readBoolean(group, 'playablePriceEligible') ?? null;
  const topPickQualityEligible = readBoolean(group, 'topPickQualityEligible') ?? null;
  const lineUtilityScore = readNumber(group, 'lineUtilityScore') ?? null;
  const effectiveAmericanOdds = readNumber(group, 'effectiveAmericanOdds') ?? null;
  const alternateLineGroupKey = readString(group, 'alternateLineGroupKey') ?? null;
  const selectedRecommendedLine = readBoolean(group, 'selectedRecommendedLine') ?? null;
  const rawSelectedSideProbability = readNumber(group, 'rawSelectedSideProbability', 'RawSelectedSideProbability') ?? null;
  const calibratedSelectedSideProbability = readNumber(group, 'calibratedSelectedSideProbability', 'CalibratedSelectedSideProbability') ?? null;
  const probabilityCalibrationMethod = readString(group, 'probabilityCalibrationMethod', 'ProbabilityCalibrationMethod') ?? null;
  const probabilityCalibrationReason = readString(group, 'probabilityCalibrationReason', 'ProbabilityCalibrationReason') ?? null;
  const modelProbabilityAvailable =
    readBoolean(group, 'modelProbabilityAvailable', 'ModelProbabilityAvailable') ??
    selectedSideProbability !== null;
  const probabilityEdge = readNumber(group, 'bestProbabilityEdge', 'BestProbabilityEdge') ?? null;
  const hasSportsbookPricing =
    readNumber(group, 'bestImpliedProbability', 'BestImpliedProbability') !== undefined ||
    readNumber(group, 'bestDecimalPrice', 'BestDecimalPrice') !== undefined ||
    readNumber(group, 'bestAmericanPrice', 'BestAmericanPrice') !== undefined;
  const showExpectedValue =
    readBoolean(group, 'showExpectedValue', 'ShowExpectedValue') ??
    (isSportsbook && hasSportsbookPricing && readNumber(group, 'bestEstimatedEv', 'BestEstimatedEv') !== undefined);
  const displayExpectedValue = showExpectedValue
    ? readNumber(group, 'displayExpectedValue', 'DisplayExpectedValue') ?? null
    : null;
  const expectedValueUnavailableReason =
    readString(group, 'expectedValueUnavailableReason', 'ExpectedValueUnavailableReason') ??
    (isDfs
      ? 'Expected value is hidden for DFS props because sportsbook odds/break-even pricing is not available.'
      : showExpectedValue
        ? undefined
        : 'Sportsbook expected value requires odds/break-even pricing.');
  const provider = resolveDisplayProvider(group, providerCategory);
  const entryCompatibilityGroup = readString(group, 'entryCompatibilityGroup', 'EntryCompatibilityGroup') ?? provider.replace(/\s+/g, '');
  const propType = readString(group, 'normalizedPropType', 'NormalizedPropType', 'propType', 'PropType') ?? 'Market';
  const propFamily = readString(group, 'propFamily', 'PropFamily') ?? null;
  const propFamilyDisplayName = readString(group, 'propFamilyDisplayName', 'PropFamilyDisplayName') ?? null;
  const event = formatEvent(readString(group, 'awayTeam', 'AwayTeam'), readString(group, 'homeTeam', 'HomeTeam'));
  const strategyWarnings = readStringArray(group, 'strategyWarnings', 'StrategyWarnings');
  const strategyReasons = readStringArray(group, 'strategyReasons', 'StrategyReasons');
  const sampleWarning = readString(group, 'sampleWarning', 'SampleWarning');
  const entryRuleWarning = readString(group, 'entryRuleWarning', 'EntryRuleWarning');
  const lifecycleStatus = readString(group, 'lifecycleStatus', 'LifecycleStatus');
  const lifecycleStatusNormalized = lifecycleStatus?.toLowerCase();
  const isExpired = readBoolean(group, 'isExpired', 'IsExpired') ?? lifecycleStatus?.toLowerCase() === 'expired';
  const isFresh = readBoolean(group, 'isFresh', 'IsFresh') ?? lifecycleStatusNormalized === 'active';
  const isStale = readBoolean(group, 'isStale', 'IsStale') ?? lifecycleStatusNormalized === 'stale';
  const line = readNumber(group, 'line', 'Line') ?? 0;
  const playerId = readString(group, 'playerId', 'PlayerId', 'canonicalPlayerId', 'CanonicalPlayerId');
  const player = readString(group, 'player', 'Player') ?? 'Unknown player';
  const eventId = readString(group, 'eventId', 'EventId');
  const eventStartTimeUtc = readString(group, 'eventStartTimeUtc', 'EventStartTimeUtc') ?? readString(group, 'eventStartTime', 'EventStartTime') ?? undefined;
  const bookmakerPrices = readArray<BookmakerPriceDto>(group, 'bookmakerPrices', 'BookmakerPrices') ?? [];
  const providerOptions = readArray<TopPickProviderOptionDto>(group, 'providerOptions', 'ProviderOptions') ?? [];
  const projectionType = readString(group, 'projectionType', 'ProjectionType');
  const projectionTypeConfidence = readString(group, 'projectionTypeConfidence', 'ProjectionTypeConfidence');
  const isAlternateMarket = readBoolean(group, 'isAlternateMarket', 'IsAlternateMarket') ?? false;
  const payoutMultiplierMayDiffer = readBoolean(group, 'payoutMultiplierMayDiffer', 'PayoutMultiplierMayDiffer') ?? false;
  const specialProjectionWarning = readString(group, 'specialProjectionWarning', 'SpecialProjectionWarning') ?? null;
  const sportsbookContextResearchOnly = readBoolean(group, 'sportsbookContextResearchOnly', 'SportsbookContextResearchOnly') ?? false;
  const isResearchOnly = readBoolean(group, 'isResearchOnly', 'IsResearchOnly') ?? sportsbookContextResearchOnly;
  const isRecommendation = readBoolean(group, 'isRecommendation') ?? false;
  const recommendationActionableForUser = readBoolean(group, 'recommendationActionableForUser') ?? false;
  const recommendationType = readString(group, 'recommendationType') ?? null;
  const resultGradingSupported = readBoolean(group, 'resultGradingSupported', 'ResultGradingSupported') ?? true;
  const resultGradingUnavailableReason = readString(group, 'resultGradingUnavailableReason', 'ResultGradingUnavailableReason') ?? null;
  const entryUnavailableReason = readString(group, 'entryUnavailableReason', 'EntryUnavailableReason') ?? null;
  const warnings = [
    ...strategyWarnings,
    ...(sampleWarning ? [sampleWarning] : []),
    ...(entryRuleWarning && providerCategory !== 'DFS' ? [entryRuleWarning] : []),
    ...(entryUnavailableReason && providerCategory !== 'DFS' ? [entryUnavailableReason] : []),
    ...(resultGradingUnavailableReason && resultGradingSupported === false ? [resultGradingUnavailableReason] : []),
    ...(specialProjectionWarning ? [specialProjectionWarning] : [])
  ]
    .map(cleanWarning)
    .filter(Boolean)
    .filter((warning, index, allWarnings) => allWarnings.indexOf(warning) === index);

  return {
    id: `${eventId ?? event}-${player}-${propType}-${line || index}-${side}-${provider}`.replace(/\s+/g, '-').toLowerCase(),
    rank: index + 1,
    playerId,
    canonicalPlayerId: playerId,
    player,
    team: readString(group, 'awayTeam', 'AwayTeam') ?? undefined,
    opponent: readString(group, 'homeTeam', 'HomeTeam') ?? undefined,
    event,
    startTime: formatStartTime(eventStartTimeUtc),
    eventStartTimeUtc,
    sport: normalizeSport(readString(group, 'sport', 'Sport')),
    competition: readString(group, 'competition', 'Competition') ?? null,
    propType,
    propFamily,
    propFamilyDisplayName,
    side,
    line,
    provider,
    providerCategory,
    isDfs,
    isSportsbook,
    showExpectedValue,
    displayExpectedValue,
    expectedValueUnavailableReason,
    rawExpectedValue,
    calibratedExpectedValue,
    evCalibrationMethod,
    evSuppressedReason,
    sportsbookContextResearchOnly,
    isResearchOnly,
    isRecommendation,
    recommendationActionableForUser,
    recommendationType,
    resultGradingSupported,
    resultGradingUnavailableReason,
    entryUnavailableReason,
    entryRuleWarning,
    entryCompatibilityGroup,
    entryProvider: provider,
    canAddToEntry: providerCategory === 'DFS' && entryActionable && !isExpired && (!lifecycleStatusNormalized || lifecycleStatusNormalized === 'active'),
    bestAmericanPrice: readNumber(group, 'bestAmericanPrice', 'BestAmericanPrice') ?? 0,
    bestDecimalPrice: readNumber(group, 'bestDecimalPrice', 'BestDecimalPrice') ?? 0,
    modelProbability: hitChance,
    hitChance,
    selectedSideProbability,
    topPickScore,
    payoutQualityScore,
    priceGateStatus,
    priceGateReason,
    playablePriceEligible,
    topPickQualityEligible,
    lineUtilityScore,
    effectiveAmericanOdds,
    alternateLineGroupKey,
    selectedRecommendedLine,
    modelVersion: readString(group, 'modelVersion') ?? null,
    probabilityModelVersion: readString(group, 'probabilityModelVersion') ?? null,
    recommendationContractVersion: readString(group, 'recommendationContractVersion') ?? null,
    rawSelectedSideProbability,
    calibratedSelectedSideProbability,
    probabilityCalibrationMethod,
    probabilityCalibrationReason,
    modelProbabilityAvailable,
    hitChanceUnavailableReason:
      readString(group, 'hitChanceUnavailableReason', 'HitChanceUnavailableReason') ??
      (modelProbabilityAvailable ? null : 'Model hit chance is unavailable for this row. Historical hit rate is shown in the chart.'),
    modelOverProbability: readNumber(group, 'modelOverProbability', 'ModelOverProbability') ?? null,
    modelUnderProbability: readNumber(group, 'modelUnderProbability', 'ModelUnderProbability') ?? null,
    probabilitySource: readString(group, 'probabilitySource', 'ProbabilitySource') ?? null,
    impliedProbability: readNumber(group, 'bestImpliedProbability', 'BestImpliedProbability') ?? null,
    probabilityEdge,
    estimatedEv: rawEstimatedEv,
    strategyClassification: normalizeStrategy(group.strategyClassification, group),
    strategyTier: readString(group, 'strategyTier', 'StrategyTier') ?? 'Research',
    trustLevel: normalizeTrust(readString(group, 'trustLevel', 'TrustLevel'), readNumber(group, 'confidenceScore', 'ConfidenceScore')),
    marketShape: readString(group, 'marketShape', 'MarketShape') ?? readString(group, 'marketClassification', 'MarketClassification') ?? 'Market row',
    historicalSampleSize: readNumber(group, 'historicalSampleSize', 'HistoricalSampleSize') ?? 0,
    freshness: isFresh ? 'Fresh' : isStale ? 'Stale' : 'Unavailable',
    lifecycleStatus,
    isFresh,
    isStale,
    isExpired,
    projectionModifier: normalizeModifier(readStringArray(group, 'riskTags', 'RiskTags'), projectionType, isAlternateMarket),
    projectionType,
    projectionTypeConfidence,
    isAlternateMarket,
    payoutMultiplierMayDiffer,
    specialProjectionWarning,
    reasons: (strategyReasons.length ? strategyReasons : ['Persisted market row']).map(cleanReason),
    warnings,
    sportsbookContext: formatReferenceContext(bookmakerPrices),
    playerHistorySummary: mapPlayerHistorySummary(group),
    alternateProviderCount: readNumber(group, 'alternateProviderCount', 'AlternateProviderCount') ?? undefined,
    providerFanoutCount: readNumber(group, 'providerFanoutCount', 'ProviderFanoutCount') ?? undefined,
    providerLines: [
      ...providerOptions.map((option) => ({
        provider: cleanProvider(readString(option, 'bestPriceBookmaker', 'BestPriceBookmaker', 'bestBookmaker', 'BestBookmaker', 'bookmaker', 'Bookmaker', 'provider', 'Provider', 'entryProvider', 'EntryProvider') ?? 'Unknown'),
        providerCategory: normalizeProviderCategory(readString(option, 'providerCategory', 'ProviderCategory')),
        line,
        overPrice: undefined,
        underPrice: undefined,
        note: formatProviderOptionPrice(option, side, line)
      })),
      ...bookmakerPrices.map((priceLine) => ({
        provider: cleanProvider(readString(priceLine, 'bookmaker', 'Bookmaker', 'bookmakerKey', 'BookmakerKey') ?? 'Unknown'),
        providerCategory: normalizeProviderCategory(readString(priceLine, 'providerCategory', 'ProviderCategory')),
        line: readNumber(priceLine, 'line', 'Line') ?? line,
        overPrice: readNumber(priceLine, 'overPrice', 'OverPrice') ?? undefined,
        underPrice: readNumber(priceLine, 'underPrice', 'UnderPrice') ?? undefined,
        note: undefined
      }))
    ]
  };
}

function mapPlayerHistorySummary(group: MarketGroupDto): PlayerHistorySummary | null {
  const summary = readField<PlayerHistorySummaryDto>(group, 'playerHistorySummary', 'PlayerHistorySummary');
  if (!summary || typeof summary !== 'object') return null;

  const statsSource = readField<Record<string, PlayerHistoryStatSummaryDto | undefined>>(summary, 'stats', 'Stats') ?? {};
  const recentRows = readArray<PlayerHistoryGameLogRowDto>(summary, 'recentGameLog', 'RecentGameLog') ?? [];

  return {
    available: readBoolean(summary, 'available', 'Available') ?? false,
    unavailableReason: readString(summary, 'unavailableReason', 'UnavailableReason') ?? null,
    provider: readString(summary, 'provider', 'Provider'),
    providerPlayerId: readString(summary, 'providerPlayerId', 'ProviderPlayerId') ?? null,
    sampleSize: readNumber(summary, 'sampleSize', 'SampleSize'),
    latestResultDate: readString(summary, 'latestResultDate', 'LatestResultDate') ?? null,
    stats: mapPlayerHistoryStats(statsSource),
    competitionBreakdown: (readArray<PlayerHistoryCompetitionBreakdownDto>(summary, 'competitionBreakdown', 'CompetitionBreakdown') ?? []).map((row) => ({
      competitionKey: readString(row, 'competitionKey', 'CompetitionKey') ?? null,
      competitionName: readString(row, 'competitionName', 'CompetitionName') ?? null,
      sampleSize: readNumber(row, 'sampleSize', 'SampleSize'),
      latestResultDate: readString(row, 'latestResultDate', 'LatestResultDate') ?? null
    })),
    recentGameLog: recentRows.map((row) => ({
      eventDate: readString(row, 'eventDate', 'EventDate') ?? null,
      competition: readString(row, 'competition', 'Competition') ?? null,
      competitionKey: readString(row, 'competitionKey', 'CompetitionKey') ?? null,
      fixtureId: readString(row, 'fixtureId', 'FixtureId') ?? null,
      stats: mapNumericHistoryStats(readField<Record<string, number | undefined>>(row, 'stats', 'Stats') ?? {})
    }))
  };
}

function resolveDisplayProvider(group: MarketGroupDto, providerCategory: ProviderCategory) {
  const entryProvider = readString(group, 'entryProvider', 'EntryProvider');
  const bestBookmaker = readString(group, 'bestBookmaker', 'BestBookmaker');
  const bestOverBookmaker = readString(group, 'bestOverBookmaker', 'BestOverBookmaker');
  const bestUnderBookmaker = readString(group, 'bestUnderBookmaker', 'BestUnderBookmaker');
  const provider = readString(group, 'provider', 'Provider');

  if (providerCategory === 'Sportsbook') {
    return cleanProvider(bestBookmaker ?? bestOverBookmaker ?? bestUnderBookmaker ?? provider ?? entryProvider ?? 'Sportsbook');
  }

  return cleanProvider(entryProvider ?? bestBookmaker ?? provider ?? 'Unknown');
}

function formatProviderOptionPrice(option: TopPickProviderOptionDto, side: MarketOpportunity['side'], line: number) {
  const american = readNumber(option, 'bestAmericanPrice', 'BestAmericanPrice');
  if (typeof american === 'number' && Number.isFinite(american) && Math.abs(american) >= 0.0001) {
    return `${side} ${line} · ${formatAmericanOdds(american)}`;
  }

  return undefined;
}

function mapPlayerHistoryStats(source: Record<string, PlayerHistoryStatSummaryDto | undefined>): PlayerHistorySummary['stats'] {
  const mapped: PlayerHistorySummary['stats'] = {};
  Object.entries(source).forEach(([key, value]) => {
    const statKey = normalizeHistoryStatKey(key);
    if (!statKey || !value) return;
    mapped[statKey] = {
      count: readNumber(value, 'count', 'Count'),
      average: readNumber(value, 'average', 'Average'),
      last5Average: readNumber(value, 'last5Average', 'Last5Average'),
      last10Average: readNumber(value, 'last10Average', 'Last10Average')
    };
  });
  return mapped;
}

function mapNumericHistoryStats(source: Record<string, number | undefined>) {
  const mapped: Partial<Record<PlayerHistoryStatKey, number>> = {};
  Object.entries(source).forEach(([key, value]) => {
    const statKey = normalizeHistoryStatKey(key);
    if (!statKey || typeof value !== 'number') return;
    mapped[statKey] = value;
  });
  return mapped;
}

function normalizeHistoryStatKey(value: string): PlayerHistoryStatKey | null {
  const normalized = value.replace(/[\s_-]+/g, '').toLowerCase();
  if (normalized === 'goals') return 'Goals';
  if (normalized === 'assists') return 'Assists';
  if (normalized === 'shots') return 'Shots';
  if (normalized === 'shotsontarget' || normalized === 'shotson target' || normalized === 'sot') return 'ShotsOnTarget';
  return null;
}

function mapPerformanceGroup(row: PerformanceGroupDto, fallbackType: string): PerformanceGroup {
  const gradedPicks = row.gradedPicks ?? 0;
  const noResults = row.noResults ?? 0;
  const unmatched = row.unmatched ?? 0;
  const pending = row.pending ?? 0;
  const totalCandidatePicks = row.totalCandidatePicks ?? gradedPicks + noResults + unmatched + pending;
  const hasGradeableSample = row.hasGradeableSample ?? gradedPicks > 0;
  const warning = row.sampleSizeWarning || gradedPicks < 30 ? 'Limited history' : undefined;
  return {
    groupType: normalizePerformanceGroupType(row.groupType, fallbackType),
    groupKey: row.groupKey,
    totalCandidatePicks,
    gradedPicks,
    wins: row.wins ?? 0,
    losses: row.losses ?? 0,
    pushes: row.pushes ?? 0,
    noResults,
    unmatched,
    pending,
    hasGradeableSample,
    hitRate: row.hitRate ?? 0,
    roi: row.simulatedRoi ?? 0,
    averageEv: row.averageEstimatedEv ?? 0,
    warning
  };
}

function normalizeProviderCategory(value?: string | null, entryActionable = false): ProviderCategory {
  if (value?.toLowerCase() === 'dfs') return 'DFS';
  if (entryActionable) return 'DFS';
  return 'Sportsbook';
}

function normalizeSide(value?: string): 'Over' | 'Under' {
  const normalized = value?.toLowerCase();
  return normalized === 'under' || normalized === 'lower' ? 'Under' : 'Over';
}

function normalizeTrust(value?: string, confidenceScore?: number | null): MarketOpportunity['trustLevel'] {
  if (value === 'Strong' || value === 'Medium' || value === 'Weak') return value;
  if (typeof confidenceScore === 'number') {
    if (confidenceScore >= 0.75) return 'Strong';
    if (confidenceScore >= 0.55) return 'Medium';
    return 'Weak';
  }
  return 'Research';
}

function normalizeStrategy(value: string | undefined, group: MarketGroupDto): StrategyClassification {
  const strategy = value ?? readString(group, 'StrategyClassification', 'strategyClassification');
  if (readBoolean(group, 'isCandidate', 'IsCandidate') || strategy === 'Candidate') return 'Candidate';
  if (readBoolean(group, 'isWatchlist', 'IsWatchlist') || strategy === 'Watchlist') return 'Watchlist';
  if (readBoolean(group, 'isResearchOnly', 'IsResearchOnly') || strategy === 'ResearchOnly') return 'ResearchOnly';
  if (readBoolean(group, 'isAvoid', 'IsAvoid') || strategy === 'Avoid') return 'Avoid';
  return strategy === 'Research' ? 'Research' : 'Research';
}

function normalizeModifier(tags?: string[], projectionType?: string, isAlternateMarket = false): ProjectionModifier {
  const normalizedProjectionType = projectionType?.trim().toLowerCase();
  if (normalizedProjectionType) {
    if (normalizedProjectionType === 'standard') return 'Normal';
    if (normalizedProjectionType === 'alternate') return 'Alternate';
    if (normalizedProjectionType === 'demon') return 'Demon';
    if (normalizedProjectionType === 'goblin') return 'Goblin';
    if (normalizedProjectionType === 'taco') return 'Taco';
    if (normalizedProjectionType === 'promo' || normalizedProjectionType === 'promotional') return 'Promo';
    if (normalizedProjectionType === 'boost' || normalizedProjectionType === 'payout boost') return 'Payout Boost';
    if (normalizedProjectionType === 'discount') return 'Discount';
    if (normalizedProjectionType === 'special') return 'Special';
    if (normalizedProjectionType === 'unknown') return 'Unknown';
  }

  const normalized = (tags ?? []).join(' ').toLowerCase();
  if (normalized.includes('demon')) return 'Demon';
  if (normalized.includes('goblin')) return 'Goblin';
  if (normalized.includes('taco')) return 'Taco';
  if (normalized.includes('alternate')) return 'Alternate';
  if (normalized.includes('discount')) return 'Discount';
  if (normalized.includes('payout boost')) return 'Payout Boost';
  if (normalized.includes('bonus lineup')) return 'Bonus Lineup';
  if (normalized.includes('promo')) return 'Promo';
  if (normalized.includes('special')) return 'Special';
  if (isAlternateMarket) return 'Alternate';
  return 'Normal';
}

function normalizePerformanceGroupType(groupType: string, fallback: string) {
  const map: Record<string, string> = {
    overall: 'Overall',
    provider: 'Provider',
    providerCategory: 'Provider',
    propType: 'Prop Type',
    strategyClassification: 'Model Segment',
    strategyTier: 'Model Segment',
    providerAndPropType: 'Provider + Prop Type'
  };

  return map[groupType] ?? fallback;
}

function cleanProvider(provider: string) {
  const map: Record<string, string> = {
    prizepicks: 'PrizePicks',
    prize_picks: 'PrizePicks',
    underdog: 'Underdog',
    underdogfantasy: 'Underdog',
    pick6: 'DraftKings Pick6',
    draftkingspick6: 'DraftKings Pick6',
    draftkings_pick6: 'DraftKings Pick6',
    'draftkings pick6': 'DraftKings Pick6',
    'betr dfs': 'Betr Picks',
    'betr picks': 'Betr Picks',
    betr_us_dfs: 'Betr Picks',
    betr_picks: 'Betr Picks',
    betrpicks: 'Betr Picks',
    draftkings: 'DraftKings',
    fanduel: 'FanDuel'
  };
  return map[provider.toLowerCase().trim()] ?? provider;
}

function normalizeSport(value?: string | null): MarketOpportunity['sport'] {
  const normalized = value?.toUpperCase();
  if (normalized === 'NBA' || normalized === 'NFL' || normalized === 'NHL' || normalized === 'MMA' || normalized === 'GOLF' || normalized === 'TENNIS' || normalized === 'SOCCER') {
    return normalized;
  }
  return 'MLB';
}

function formatEvent(away?: string | null, home?: string | null) {
  if (away && home) return `${away} @ ${home}`;
  return away ?? home ?? 'Upcoming event';
}

function formatStartTime(value?: string | null) {
  if (!value) return 'Time TBD';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'Time TBD';
  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit'
  }).format(date);
}

function formatReferenceContext(lines?: MarketGroupDto['bookmakerPrices']) {
  const sportsbookLines = (lines ?? []).filter((line) => normalizeProviderCategory(readString(line, 'providerCategory', 'ProviderCategory')) === 'Sportsbook');
  if (!sportsbookLines.length) return 'No sportsbook reference lines loaded';
  const prices = sportsbookLines
    .slice(0, 2)
    .map((line) => `${cleanProvider(readString(line, 'bookmaker', 'Bookmaker', 'bookmakerKey', 'BookmakerKey') ?? 'Book')} ${readNumber(line, 'line', 'Line') ?? '-'}`)
    .join(', ');
  return prices;
}

function cleanWarning(value: string) {
  if (/^(none|null|n\/a)$/i.test(value.trim())) {
    return '';
  }

  return value
    .replace('LowSample', 'Low sample')
    .replace('Sportsbook rows are market context for this DFS-only user. Do not mix sportsbook legs across books.', 'Reference lines are not entry legs.')
    .replace('SmallSample', 'Limited history')
    .replace('Low sample size', 'Low sample')
    .replace('Confirm final payout in provider app', 'Payout may vary');
}

function cleanReason(value: string) {
  return value.replace('Promo-adjusted', 'Modifier-aware context');
}

type ApiRecord = Record<string, unknown>;

function readField<T>(record: object, ...keys: string[]): T | undefined {
  const source = record as ApiRecord;
  for (const key of keys) {
    const value = source[key];
    if (value !== undefined && value !== null) {
      return value as T;
    }
  }
  return undefined;
}

function readString(record: object, ...keys: string[]) {
  const value = readField<unknown>(record, ...keys);
  return typeof value === 'string' && value.trim() ? value : undefined;
}

function readNumber(record: object, ...keys: string[]) {
  const value = readField<unknown>(record, ...keys);
  if (typeof value === 'number') return value;
  if (typeof value === 'string' && value.trim()) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : undefined;
  }
  return undefined;
}

function readBoolean(record: object, ...keys: string[]) {
  const value = readField<unknown>(record, ...keys);
  if (typeof value === 'boolean') return value;
  if (typeof value === 'string') return value.toLowerCase() === 'true';
  return undefined;
}

function readArray<T>(record: object, ...keys: string[]) {
  const value = readField<unknown>(record, ...keys);
  return Array.isArray(value) ? value as T[] : undefined;
}

function readStringArray(record: object, ...keys: string[]) {
  return readArray<string>(record, ...keys)?.filter((value): value is string => typeof value === 'string') ?? [];
}
