export interface PerformanceGroup {
  groupType: string;
  groupKey: string;
  gradedPicks: number;
  wins: number;
  losses: number;
  pushes: number;
  noResults?: number;
  unmatched?: number;
  pending?: number;
  hitRate: number;
  roi: number;
  averageEv: number;
  warning?: string;
}
