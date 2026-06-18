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
