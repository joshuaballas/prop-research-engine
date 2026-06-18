import { mockSports } from '../data/mockSports';
import type { MarketOpportunity } from '../types/markets';
import type { SportCode, SportOption } from '../types/sports';

const sportMetadataByCode = new Map(mockSports.map((sport) => [sport.code, sport]));

export function getAvailableSportCodes(markets: MarketOpportunity[]) {
  return new Set(markets.map((market) => market.sport));
}

export function buildAvailableSportOptions(availableSportCodes: Set<Exclude<SportCode, 'ALL'>>): SportOption[] {
  if (!availableSportCodes.size) {
    return [];
  }

  const options = mockSports
    .filter((sport) => sport.code !== 'ALL' && availableSportCodes.has(sport.code))
    .map(toStatuslessSportOption);

  const allOption = toStatuslessSportOption(sportMetadataByCode.get('ALL') ?? mockSports[0]);
  return [allOption, ...options];
}

function toStatuslessSportOption(sport: SportOption): SportOption {
  return {
    code: sport.code,
    label: sport.label,
    icon: sport.icon,
    candidateCount: sport.candidateCount
  };
}
