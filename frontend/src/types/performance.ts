export interface PerformanceGroup {
  groupType: string;
  groupKey: string;
  totalCandidatePicks: number;
  gradedPicks: number;
  wins: number;
  losses: number;
  pushes: number;
  noResults?: number;
  unmatched?: number;
  pending?: number;
  hasGradeableSample?: boolean;
  hitRate: number;
  roi: number;
  averageEv: number;
  warning?: string;
}
