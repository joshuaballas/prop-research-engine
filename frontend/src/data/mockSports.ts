import type { SportOption } from '../types/sports';

export const mockSports: SportOption[] = [
  { code: 'ALL', label: 'All', icon: '◆', candidateCount: 34, status: 'Live' },
  { code: 'MLB', label: 'MLB', icon: '⚾', candidateCount: 14, status: 'Live' },
  { code: 'NBA', label: 'NBA', icon: '🏀', candidateCount: 5, status: 'Soon' },
  { code: 'NFL', label: 'NFL', icon: '🏈', candidateCount: 3, status: 'Soon' },
  { code: 'NHL', label: 'NHL', icon: '🏒', candidateCount: 2, status: 'No data' },
  { code: 'MMA', label: 'MMA', icon: '🥊', candidateCount: 4, status: 'Live' },
  { code: 'GOLF', label: 'Golf', icon: '⛳', candidateCount: 2, status: 'Live' },
  { code: 'TENNIS', label: 'Tennis', icon: '🎾', candidateCount: 3, status: 'Live' },
  { code: 'SOCCER', label: 'Soccer', icon: '⚽', candidateCount: 1, status: 'Soon' }
];
