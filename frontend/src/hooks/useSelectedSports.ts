import { useCallback, useMemo, useState } from 'react';
import type { SportCode } from '../types/sports';

export function useSelectedSports(defaultSelection: SportCode[] = ['ALL']) {
  const [selectedSports, setSelectedSports] = useState<SportCode[]>(defaultSelection);

  const toggleSport = useCallback((sport: SportCode) => {
    setSelectedSports((current) => {
      if (sport === 'ALL') return ['ALL'];
      const next = current.includes(sport)
        ? current.filter((item) => item !== sport)
        : [...current.filter((item) => item !== 'ALL'), sport];
      return next.length === 0 ? ['ALL'] : next;
    });
  }, []);

  const includesSport = useMemo(
    () => (sport: SportCode) => selectedSports.includes('ALL') || selectedSports.includes(sport),
    [selectedSports]
  );

  return { selectedSports, toggleSport, includesSport };
}
