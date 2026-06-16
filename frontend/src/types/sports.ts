export type SportCode = 'ALL' | 'MLB' | 'NBA' | 'NFL' | 'NHL' | 'MMA' | 'GOLF' | 'TENNIS' | 'SOCCER';

export interface SportOption {
  code: SportCode;
  label: string;
  icon: string;
  candidateCount: number;
  status?: 'Live' | 'Soon' | 'No data';
}
