import { apiGet, buildQuery } from './client';
import type { PlayerResearchCurrentLinesResponseDto, PlayerSearchResultDto } from './types';

export function searchPlayers(query: string, limit = 5, signal?: AbortSignal) {
  return apiGet<PlayerSearchResultDto[]>(
    `/api/players/search${buildQuery({ q: query, limit })}`,
    signal
  );
}

export function getPlayerResearchCurrentLines(playerId: string, take = 50, signal?: AbortSignal) {
  return apiGet<PlayerResearchCurrentLinesResponseDto>(
    `/api/player-research/current-lines${buildQuery({ playerId, take })}`,
    signal
  );
}
