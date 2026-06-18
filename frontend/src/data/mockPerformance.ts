import type { PerformanceGroup } from '../types/performance';

export type { PerformanceGroup };

export const mockPerformance: PerformanceGroup[] = [
  { groupType: 'Overall', groupKey: 'DFS candidates', gradedPicks: 200, wins: 108, losses: 88, pushes: 4, hitRate: 0.551, roi: 0.087, averageEv: 0.096 },
  { groupType: 'Provider', groupKey: 'PrizePicks', gradedPicks: 82, wins: 46, losses: 34, pushes: 2, hitRate: 0.575, roi: 0.112, averageEv: 0.104 },
  { groupType: 'Provider', groupKey: 'Underdog', gradedPicks: 61, wins: 32, losses: 28, pushes: 1, hitRate: 0.533, roi: 0.041, averageEv: 0.082 },
  { groupType: 'Provider', groupKey: 'Betr Picks', gradedPicks: 14, wins: 8, losses: 6, pushes: 0, hitRate: 0.571, roi: 0.131, averageEv: 0.117, warning: 'Limited history' },
  { groupType: 'Sport', groupKey: 'MLB', gradedPicks: 121, wins: 68, losses: 50, pushes: 3, hitRate: 0.576, roi: 0.104, averageEv: 0.1 },
  { groupType: 'Sport', groupKey: 'NBA', gradedPicks: 42, wins: 21, losses: 20, pushes: 1, hitRate: 0.512, roi: 0.019, averageEv: 0.074 },
  { groupType: 'Sport', groupKey: 'Tennis', gradedPicks: 18, wins: 10, losses: 8, pushes: 0, hitRate: 0.556, roi: 0.064, averageEv: 0.083, warning: 'Limited history' },
  { groupType: 'Prop Type', groupKey: 'MLB Total Bases', gradedPicks: 97, wins: 55, losses: 40, pushes: 2, hitRate: 0.579, roi: 0.097, averageEv: 0.091 },
  { groupType: 'Prop Type', groupKey: 'MLB Pitcher Strikeouts', gradedPicks: 24, wins: 15, losses: 9, pushes: 0, hitRate: 0.625, roi: 0.146, averageEv: 0.123, warning: 'Limited history' },
  { groupType: 'Confidence', groupKey: 'Strong', gradedPicks: 74, wins: 45, losses: 27, pushes: 2, hitRate: 0.616, roi: 0.128, averageEv: 0.121 },
  { groupType: 'Confidence', groupKey: 'Medium', gradedPicks: 91, wins: 48, losses: 41, pushes: 2, hitRate: 0.539, roi: 0.061, averageEv: 0.086 },
  { groupType: 'Confidence', groupKey: 'Weak', gradedPicks: 35, wins: 15, losses: 20, pushes: 0, hitRate: 0.429, roi: -0.036, averageEv: 0.052, warning: 'Research only' },
  { groupType: 'Strategy', groupKey: 'ConservativeCandidate', gradedPicks: 53, wins: 35, losses: 18, pushes: 0, hitRate: 0.66, roi: 0.121, averageEv: 0.108 },
  { groupType: 'Provider + Sport', groupKey: 'PrizePicks MLB', gradedPicks: 45, wins: 27, losses: 17, pushes: 1, hitRate: 0.614, roi: 0.132, averageEv: 0.119 }
];
