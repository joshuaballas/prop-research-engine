import type { MarketOpportunity } from '../types/markets';
import type { SportCode } from '../types/sports';

export function marketDisplayLabel(market: Pick<MarketOpportunity, 'sport' | 'competition'>) {
  return sportDisplayLabel(market.sport, market.competition);
}

export function sportDisplayLabel(sport: Exclude<SportCode, 'ALL'> | string, competition?: string | null) {
  const normalizedSport = sport.toUpperCase();
  const cleanCompetition = competition?.trim();

  if (normalizedSport === 'SOCCER') {
    return cleanCompetition || 'Soccer';
  }

  return cleanCompetition || normalizedSport;
}
