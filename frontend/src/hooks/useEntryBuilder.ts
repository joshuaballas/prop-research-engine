import { useState } from 'react';
import { mockEntryRules } from '../data/mockEntryRules';
import type { EntryLeg } from '../types/entryBuilder';
import type { MarketOpportunity } from '../types/markets';
import { canAddLeg } from '../utils/entryRules';
import { useToast } from './useToast';

export function useEntryBuilder() {
  const [legs, setLegs] = useState<EntryLeg[]>([]);
  const [amount, setAmount] = useState(25);
  const { showToast } = useToast();

  const addLeg = (market: MarketOpportunity) => {
    const result = canAddLeg(legs, market, mockEntryRules);
    if (!result.allowed) {
      showToast(result.reason, 'warning');
      return;
    }
    setLegs((current) => [...current, market]);
    showToast(`${market.player} added. ${result.reason}`, 'success');
  };

  const removeLeg = (id: string) => setLegs((current) => current.filter((leg) => leg.id !== id));
  const clear = () => {
    setLegs([]);
    showToast('Entry cleared.', 'info');
  };

  return { legs, amount, setAmount, addLeg, removeLeg, clear };
}
