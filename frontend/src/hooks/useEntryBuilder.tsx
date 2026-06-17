import { createContext, useCallback, useContext, useMemo, useReducer, type PropsWithChildren } from 'react';
import { mockEntryRules } from '../data/mockEntryRules';
import type { EntryLeg } from '../types/entryBuilder';
import type { MarketOpportunity } from '../types/markets';
import { canAddLeg } from '../utils/entryRules';
import { useToast } from './useToast';

interface EntryBuilderState {
  selectedLegs: EntryLeg[];
  entryAmount: number;
  isDrawerOpen: boolean;
  showAllCandidates: boolean;
}

type EntryBuilderAction =
  | { type: 'addLeg'; leg: EntryLeg }
  | { type: 'removeLeg'; legId: string }
  | { type: 'clearEntry' }
  | { type: 'setEntryAmount'; amount: number }
  | { type: 'openEntryBuilder' }
  | { type: 'closeEntryBuilder' }
  | { type: 'setShowAllCandidates'; value: boolean }
  | { type: 'toggleShowAllCandidates' };

interface EntryBuilderContextValue {
  legs: EntryLeg[];
  selectedLegs: EntryLeg[];
  amount: number;
  entryAmount: number;
  lockedProviderKey?: string;
  lockedProviderName?: string;
  isDrawerOpen: boolean;
  showAllCandidates: boolean;
  addLeg: (market: MarketOpportunity) => boolean;
  removeLeg: (legId: string) => void;
  toggleLeg: (market: MarketOpportunity) => boolean;
  clear: () => void;
  clearEntry: () => void;
  setAmount: (amount: number) => void;
  setEntryAmount: (amount: number) => void;
  openEntryBuilder: () => void;
  closeEntryBuilder: () => void;
  setShowAllCandidates: (value: boolean) => void;
  toggleShowAllCandidates: () => void;
  isSelected: (legId: string) => boolean;
  isCompatible: (market: MarketOpportunity) => boolean;
}

const initialState: EntryBuilderState = {
  selectedLegs: [],
  entryAmount: 25,
  isDrawerOpen: false,
  showAllCandidates: false
};

const EntryBuilderContext = createContext<EntryBuilderContextValue | null>(null);

export function EntryBuilderProvider({ children }: PropsWithChildren) {
  const [state, dispatch] = useReducer(entryBuilderReducer, initialState);
  const { showToast } = useToast();
  const lockedProviderName = state.selectedLegs[0]?.entryProvider;
  const lockedProviderKey = state.selectedLegs[0]?.entryCompatibilityGroup;

  const addLeg = useCallback(
    (market: MarketOpportunity) => {
      if (state.selectedLegs.some((leg) => leg.id === market.id)) {
        return true;
      }

      const result = canAddLeg(state.selectedLegs, market, mockEntryRules);
      if (!result.allowed) {
        showToast(`This entry is locked to ${lockedProviderName ?? state.selectedLegs[0]?.entryProvider ?? 'the selected provider'}.`, 'warning');
        return false;
      }

      dispatch({ type: 'addLeg', leg: mapMarketToEntryLeg(market) });
      return true;
    },
    [lockedProviderName, showToast, state.selectedLegs]
  );

  const removeLeg = useCallback((legId: string) => {
    dispatch({ type: 'removeLeg', legId });
  }, []);

  const toggleLeg = useCallback(
    (market: MarketOpportunity) => {
      if (state.selectedLegs.some((leg) => leg.id === market.id)) {
        dispatch({ type: 'removeLeg', legId: market.id });
        return true;
      }

      return addLeg(market);
    },
    [addLeg, state.selectedLegs]
  );

  const clearEntry = useCallback(() => {
    dispatch({ type: 'clearEntry' });
  }, []);

  const value = useMemo<EntryBuilderContextValue>(
    () => ({
      legs: state.selectedLegs,
      selectedLegs: state.selectedLegs,
      amount: state.entryAmount,
      entryAmount: state.entryAmount,
      lockedProviderKey,
      lockedProviderName,
      isDrawerOpen: state.isDrawerOpen,
      showAllCandidates: state.showAllCandidates,
      addLeg,
      removeLeg,
      toggleLeg,
      clear: clearEntry,
      clearEntry,
      setAmount: (amount: number) => dispatch({ type: 'setEntryAmount', amount }),
      setEntryAmount: (amount: number) => dispatch({ type: 'setEntryAmount', amount }),
      openEntryBuilder: () => dispatch({ type: 'openEntryBuilder' }),
      closeEntryBuilder: () => dispatch({ type: 'closeEntryBuilder' }),
      setShowAllCandidates: (nextValue: boolean) => dispatch({ type: 'setShowAllCandidates', value: nextValue }),
      toggleShowAllCandidates: () => dispatch({ type: 'toggleShowAllCandidates' }),
      isSelected: (legId: string) => state.selectedLegs.some((leg) => leg.id === legId),
      isCompatible: (market: MarketOpportunity) => !lockedProviderKey || market.entryCompatibilityGroup === lockedProviderKey
    }),
    [addLeg, clearEntry, lockedProviderKey, lockedProviderName, removeLeg, state.entryAmount, state.isDrawerOpen, state.selectedLegs, state.showAllCandidates, toggleLeg]
  );

  return <EntryBuilderContext.Provider value={value}>{children}</EntryBuilderContext.Provider>;
}

export function useEntryBuilder() {
  const context = useContext(EntryBuilderContext);
  if (!context) {
    throw new Error('useEntryBuilder must be used inside EntryBuilderProvider.');
  }

  return context;
}

function entryBuilderReducer(state: EntryBuilderState, action: EntryBuilderAction): EntryBuilderState {
  switch (action.type) {
    case 'addLeg':
      return {
        ...state,
        selectedLegs: [...state.selectedLegs, action.leg],
        isDrawerOpen: true,
        showAllCandidates: false
      };
    case 'removeLeg': {
      const selectedLegs = state.selectedLegs.filter((leg) => leg.id !== action.legId);
      return {
        ...state,
        selectedLegs,
        isDrawerOpen: selectedLegs.length ? state.isDrawerOpen : false,
        showAllCandidates: selectedLegs.length ? state.showAllCandidates : false
      };
    }
    case 'clearEntry':
      return {
        ...state,
        selectedLegs: [],
        isDrawerOpen: false,
        showAllCandidates: false
      };
    case 'setEntryAmount':
      return { ...state, entryAmount: action.amount };
    case 'openEntryBuilder':
      return { ...state, isDrawerOpen: true };
    case 'closeEntryBuilder':
      return { ...state, isDrawerOpen: false };
    case 'setShowAllCandidates':
      return { ...state, showAllCandidates: action.value };
    case 'toggleShowAllCandidates':
      return { ...state, showAllCandidates: !state.showAllCandidates };
    default:
      return state;
  }
}

export function mapMarketToEntryLeg(market: MarketOpportunity): EntryLeg {
  return market;
}
