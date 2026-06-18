import type { MarketOpportunity, ProjectionModifier, ProviderCategory, StrategyClassification } from '../types/markets';
import type { PerformanceGroup } from '../types/performance';
import type { BookmakerPriceDto, MarketGroupDto, MarketPerformanceSummaryDto, PerformanceGroupDto } from './types';

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
    ...(summary.groups.byStrategyClassification ?? []).map((row) => mapPerformanceGroup(row, 'Confidence')),
    ...(summary.groups.byStrategyTier ?? []).map((row) => mapPerformanceGroup(row, 'Strategy')),
    ...(summary.groups.byProviderAndPropType ?? []).map((row) => mapPerformanceGroup(row, 'Provider + Sport'))
  ];
}

function mapMarketGroupToOpportunity(group: MarketGroupDto, index: number, sourcePrefix: string): MarketOpportunity {
  const side = normalizeSide(readString(group, 'bestSide', 'BestSide'));
  const entryActionable = readBoolean(group, 'entryActionableForUser', 'EntryActionableForUser') ?? readBoolean(group, 'isActionableForUser', 'IsActionableForUser') ?? false;
  const providerCategory = normalizeProviderCategory(
    readString(group, 'entryProviderCategory', 'EntryProviderCategory') ?? readString(group, 'providerCategory', 'ProviderCategory'),
    entryActionable
  );
  const provider = cleanProvider(
    readString(group, 'entryProvider', 'EntryProvider') ??
      readString(group, 'bestBookmaker', 'BestBookmaker') ??
      readString(group, 'providerCategory', 'ProviderCategory') ??
      'Unknown'
  );
  const entryCompatibilityGroup = readString(group, 'entryCompatibilityGroup', 'EntryCompatibilityGroup') ?? provider.replace(/\s+/g, '');
  const propType = readString(group, 'normalizedPropType', 'NormalizedPropType') ?? 'Market';
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
  const player = readString(group, 'player', 'Player') ?? 'Unknown player';
  const eventId = readString(group, 'eventId', 'EventId');
  const bookmakerPrices = readArray<BookmakerPriceDto>(group, 'bookmakerPrices', 'BookmakerPrices') ?? [];
  const warnings = [
    ...strategyWarnings,
    ...(sampleWarning ? [sampleWarning] : []),
    ...(entryRuleWarning && providerCategory !== 'DFS' ? [entryRuleWarning] : [])
  ].map(cleanWarning);

  return {
    id: `${eventId ?? event}-${player}-${propType}-${line || index}-${side}-${provider}`.replace(/\s+/g, '-').toLowerCase(),
    rank: index + 1,
    player,
    team: readString(group, 'awayTeam', 'AwayTeam') ?? undefined,
    opponent: readString(group, 'homeTeam', 'HomeTeam') ?? undefined,
    event,
    startTime: formatStartTime(readString(group, 'eventStartTimeUtc', 'EventStartTimeUtc') ?? readString(group, 'eventStartTime', 'EventStartTime')),
    sport: normalizeSport(readString(group, 'sport', 'Sport')),
    propType,
    side,
    line,
    provider,
    providerCategory,
    entryCompatibilityGroup,
    entryProvider: provider,
    canAddToEntry: providerCategory === 'DFS' && entryActionable && !isExpired && (!lifecycleStatusNormalized || lifecycleStatusNormalized === 'active'),
    bestAmericanPrice: readNumber(group, 'bestAmericanPrice', 'BestAmericanPrice') ?? 0,
    bestDecimalPrice: readNumber(group, 'bestDecimalPrice', 'BestDecimalPrice') ?? 0,
    modelProbability: readNumber(group, 'bestProbability', 'BestProbability') ?? 0,
    impliedProbability: readNumber(group, 'bestImpliedProbability', 'BestImpliedProbability') ?? 0,
    probabilityEdge: readNumber(group, 'bestProbabilityEdge', 'BestProbabilityEdge') ?? 0,
    estimatedEv: readNumber(group, 'bestEstimatedEv', 'BestEstimatedEv') ?? 0,
    strategyClassification: normalizeStrategy(group.strategyClassification, group),
    strategyTier: readString(group, 'strategyTier', 'StrategyTier') ?? 'Research',
    trustLevel: normalizeTrust(readString(group, 'trustLevel', 'TrustLevel'), readNumber(group, 'confidenceScore', 'ConfidenceScore')),
    marketShape: readString(group, 'marketShape', 'MarketShape') ?? readString(group, 'marketClassification', 'MarketClassification') ?? 'Market row',
    historicalSampleSize: readNumber(group, 'historicalSampleSize', 'HistoricalSampleSize') ?? 0,
    freshness: isFresh ? 'Fresh' : isStale ? 'Stale' : 'Unavailable',
    projectionModifier: normalizeModifier(readStringArray(group, 'riskTags', 'RiskTags')),
    reasons: (strategyReasons.length ? strategyReasons : ['Persisted market row']).map(cleanReason),
    warnings,
    sportsbookContext: formatReferenceContext(bookmakerPrices),
    providerLines: bookmakerPrices.map((priceLine) => ({
      provider: cleanProvider(readString(priceLine, 'bookmaker', 'Bookmaker', 'bookmakerKey', 'BookmakerKey') ?? 'Unknown'),
      providerCategory: normalizeProviderCategory(readString(priceLine, 'providerCategory', 'ProviderCategory')),
      line: readNumber(priceLine, 'line', 'Line') ?? line,
      overPrice: readNumber(priceLine, 'overPrice', 'OverPrice') ?? undefined,
      underPrice: readNumber(priceLine, 'underPrice', 'UnderPrice') ?? undefined,
      note: readString(priceLine, 'region', 'Region')
    }))
  };
}

function mapPerformanceGroup(row: PerformanceGroupDto, fallbackType: string): PerformanceGroup {
  const gradedPicks = row.gradedPicks ?? 0;
  const warning = row.sampleSizeWarning || gradedPicks < 30 ? 'Limited history' : undefined;
  return {
    groupType: normalizePerformanceGroupType(row.groupType, fallbackType),
    groupKey: row.groupKey,
    gradedPicks,
    wins: row.wins ?? 0,
    losses: row.losses ?? 0,
    pushes: row.pushes ?? 0,
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

function normalizeModifier(tags?: string[]): ProjectionModifier {
  const normalized = (tags ?? []).join(' ').toLowerCase();
  if (normalized.includes('demon')) return 'Demon';
  if (normalized.includes('goblin')) return 'Goblin';
  if (normalized.includes('discount')) return 'Discount';
  if (normalized.includes('payout boost')) return 'Payout Boost';
  if (normalized.includes('bonus lineup')) return 'Bonus Lineup';
  if (normalized.includes('promo')) return 'Unknown Promo';
  return 'Normal';
}

function normalizePerformanceGroupType(groupType: string, fallback: string) {
  const map: Record<string, string> = {
    overall: 'Overall',
    provider: 'Provider',
    providerCategory: 'Provider',
    propType: 'Prop Type',
    strategyClassification: 'Confidence',
    strategyTier: 'Strategy',
    providerAndPropType: 'Provider + Sport'
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
  return value
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
