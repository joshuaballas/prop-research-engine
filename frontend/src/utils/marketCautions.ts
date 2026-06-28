import type { MarketOpportunity } from '../types/markets';

export type CautionKey =
  | 'No longer available'
  | 'Expired'
  | 'Started'
  | 'Locked'
  | 'Provider issue'
  | 'Stale'
  | 'Market context'
  | 'Low sample'
  | 'Pricing caution'
  | 'Model caution';

export interface MarketCaution {
  key: CautionKey;
  label: string;
  description: string;
}

const priority: CautionKey[] = [
  'No longer available',
  'Expired',
  'Started',
  'Locked',
  'Provider issue',
  'Stale',
  'Market context',
  'Low sample',
  'Pricing caution',
  'Model caution'
];

const descriptions: Record<CautionKey, string> = {
  'No longer available': 'Provider line appears to be pulled or unavailable.',
  Expired: 'Line is expired and should be reviewed only.',
  Started: 'Game has started or the line is no longer addable.',
  Locked: 'Entry is locked to another provider.',
  'Provider issue': 'Provider availability or line status may be unreliable.',
  Stale: 'Line may be stale based on the latest refresh.',
  'Market context': 'This row is market context and should not be added directly.',
  'Low sample': 'Limited historical sample. Treat the estimate with extra caution.',
  'Pricing caution': 'Pricing signal is not strong enough for a primary recommendation.',
  'Model caution': 'Similar rows showed weak early performance testing.'
};

export function isPlayableMarket(market: MarketOpportunity) {
  return isEntryBuilderAddableMarket(market);
}

export function isEntryBuilderAddableMarket(market: MarketOpportunity) {
  return market.providerCategory === 'DFS' && !isLineNoLongerAvailable(market);
}

export function isLineNoLongerAvailable(market: MarketOpportunity) {
  const lifecycleStatus = String(market.lifecycleStatus ?? '').trim().toLowerCase();
  return Boolean(
    market.isExpired ||
    market.isStale ||
    market.freshness === 'Stale' ||
    lifecycleStatus === 'expired' ||
    lifecycleStatus === 'awaitingresults' ||
    lifecycleStatus === 'completed'
  );
}

export function entryBuilderUnavailableReason(market: MarketOpportunity) {
  if (market.providerCategory === 'Sportsbook') {
    return 'Sportsbook line. Entry Builder supports DFS entries only.';
  }

  if (isLineNoLongerAvailable(market)) {
    return 'Line no longer available.';
  }

  return market.entryRuleWarning ?? market.entryUnavailableReason ?? 'Entry Builder unavailable for this row.';
}

export function getMarketCautions(market: MarketOpportunity, options: { locked?: boolean } = {}) {
  const keys = new Set<CautionKey>();

  if (options.locked) keys.add('Locked');
  if (market.providerCategory === 'Sportsbook' && !market.isRecommendation) keys.add('Market context');
  if (market.freshness === 'Stale') keys.add('Stale');
  if (market.freshness === 'Unavailable') keys.add('Provider issue');
  if (market.estimatedEv <= 0) keys.add('Pricing caution');
  if (market.historicalSampleSize > 0 && market.historicalSampleSize < 35) keys.add('Low sample');
  if (market.strategyClassification === 'Research' || market.strategyClassification === 'ResearchOnly') keys.add('Market context');
  if (market.strategyClassification === 'Avoid') keys.add('Model caution');

  for (const raw of [...(market.warnings ?? []), ...(market.reasons ?? [])]) {
    const key = cautionKeyFromText(raw);
    if (key) keys.add(key);
  }

  return priority
    .filter((key) => keys.has(key))
    .map((key) => ({
      key,
      label: key,
      description: descriptions[key]
    }));
}

export function getOperationalMarketCautions(market: MarketOpportunity, options: { locked?: boolean } = {}) {
  const operationalKeys = new Set<CautionKey>([
    'No longer available',
    'Expired',
    'Started',
    'Locked',
    'Provider issue',
    'Stale'
  ]);

  return getMarketCautions(market, options).filter((caution) => operationalKeys.has(caution.key));
}

function cautionKeyFromText(value: string | null | undefined): CautionKey | null {
  const text = String(value ?? '').trim();
  if (!text || ['none', 'null', 'undefined'].includes(text.toLowerCase())) return null;

  const normalized = text
    .replace(/[_-]+/g, ' ')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/\s+/g, ' ')
    .trim()
    .toLowerCase();

  if (normalized.includes('provider pulled') || normalized.includes('pulled') || normalized.includes('no longer available')) return 'No longer available';
  if (normalized.includes('expired')) return 'Expired';
  if (normalized.includes('event has started') || normalized.includes('started')) return 'Started';
  if (normalized.includes('locked')) return 'Locked';
  if (normalized.includes('provider unavailable') || normalized.includes('provider issue') || normalized.includes('provider limited')) return 'Provider issue';
  if (normalized.includes('stale')) return 'Stale';
  if (normalized.includes('research row') || normalized.includes('research only')) return 'Market context';
  if (normalized.includes('low sample') || normalized.includes('insufficient historical sample') || normalized.includes('sample is thin')) return 'Low sample';
  if (normalized.includes('non positive ev') || normalized.includes('non-positive ev')) return 'Pricing caution';
  if (normalized.includes('underperformed') || normalized.includes('backtest') || normalized.includes('model caution')) return 'Model caution';

  return null;
}
