export interface PerformanceGroup {
  groupType: string;
  groupKey: string;
  gradedPicks: number;
  wins: number;
  losses: number;
  pushes: number;
  unmatched?: number;
  pending?: number;
  hasGradeableSample?: boolean;
  hitRate: number;
  roi: number;
  averageEv: number;
  warning?: string;
}
