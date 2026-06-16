export interface PerformanceGroup {
  groupType: string;
  groupKey: string;
  gradedPicks: number;
  wins: number;
  losses: number;
  pushes: number;
  hitRate: number;
  roi: number;
  averageEv: number;
  warning?: string;
}

export const mockPerformance: PerformanceGroup[] = [
  { groupType: 'Overall', groupKey: 'DFS candidates', gradedPicks: 200, wins: 108, losses: 88, pushes: 4, hitRate: 0.551, roi: 0.087, averageEv: 0.096 },
  { groupType: 'Provider', groupKey: 'PrizePicks', gradedPicks: 82, wins: 46, losses: 34, pushes: 2, hitRate: 0.575, roi: 0.112, averageEv: 0.104 },
  { groupType: 'Provider', groupKey: 'Underdog', gradedPicks: 61, wins: 32, losses: 28, pushes: 1, hitRate: 0.533, roi: 0.041, averageEv: 0.082 },
  { groupType: 'Provider', groupKey: 'Betr Picks', gradedPicks: 14, wins: 8, losses: 6, pushes: 0, hitRate: 0.571, roi: 0.131, averageEv: 0.117, warning: 'Low sample size' },
  { groupType: 'Prop Type', groupKey: 'MLB Total Bases', gradedPicks: 97, wins: 55, losses: 40, pushes: 2, hitRate: 0.579, roi: 0.097, averageEv: 0.091 },
  { groupType: 'Prop Type', groupKey: 'MLB Pitcher Strikeouts', gradedPicks: 24, wins: 15, losses: 9, pushes: 0, hitRate: 0.625, roi: 0.146, averageEv: 0.123, warning: 'Low sample size' },
  { groupType: 'Strategy', groupKey: 'ConservativeCandidate', gradedPicks: 53, wins: 35, losses: 18, pushes: 0, hitRate: 0.66, roi: 0.121, averageEv: 0.108 },
  { groupType: 'Provider + Sport', groupKey: 'PrizePicks MLB', gradedPicks: 45, wins: 27, losses: 17, pushes: 1, hitRate: 0.614, roi: 0.132, averageEv: 0.119 }
];
