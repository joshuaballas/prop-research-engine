import { createContext, useCallback, useContext, useEffect, useMemo, useReducer, useState, type PropsWithChildren } from 'react';
import { getEntryRules } from '../api/entryRules';
import { mockEntryRules } from '../data/mockEntryRules';
import type { EntryLeg, EntryRule } from '../types/entryBuilder';
import type { MarketOpportunity } from '../types/markets';
import { canAddLeg } from '../utils/entryRules';
import { useToast } from './useToast';

interface EntryBuilderState {
  selectedLegs: EntryLeg[];
  entryAmount: number;
  selectedEntryType: string;
  isDrawerOpen: boolean;
  showAllCandidates: boolean;
}

type EntryBuilderAction =
  | { type: 'addLeg'; leg: EntryLeg }
  | { type: 'removeLeg'; legId: string }
  | { type: 'clearEntry' }
  | { type: 'setEntryAmount'; amount: number }
  | { type: 'setSelectedEntryType'; entryType: string }
  | { type: 'openEntryBuilder' }
  | { type: 'closeEntryBuilder' }
  | { type: 'setShowAllCandidates'; value: boolean }
  | { type: 'toggleShowAllCandidates' };

interface EntryBuilderContextValue {
  legs: EntryLeg[];
  selectedLegs: EntryLeg[];
  amount: number;
  entryAmount: number;
  selectedEntryType: string;
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
  setSelectedEntryType: (entryType: string) => void;
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
  selectedEntryType: '',
  isDrawerOpen: false,
  showAllCandidates: false
};

const ENTRY_BUILDER_STORAGE_KEY = 'sportsbetting.entryBuilder.v1';
const ENTRY_BUILDER_STORAGE_VERSION = 1;

const EntryBuilderContext = createContext<EntryBuilderContextValue | null>(null);

export function EntryBuilderProvider({ children }: PropsWithChildren) {
  const [state, dispatch] = useReducer(entryBuilderReducer, undefined, loadPersistedEntryBuilderState);
  const [rules, setRules] = useState<EntryRule[]>(mockEntryRules);
  const { showToast } = useToast();
  const lockedProviderName = state.selectedLegs[0]?.entryProvider;
  const lockedProviderKey = state.selectedLegs[0]?.entryCompatibilityGroup;

  const addLeg = useCallback(
    (market: MarketOpportunity) => {
      if (state.selectedLegs.some((leg) => leg.id === market.id)) {
        showToast('This leg is already in your entry.', 'error');
        return false;
      }

      const result = canAddLeg(state.selectedLegs, market, rules);
      if (!result.allowed) {
        showToast(result.reason, 'error');
        return false;
      }

      dispatch({ type: 'addLeg', leg: mapMarketToEntryLeg(market) });
      return true;
    },
    [rules, showToast, state.selectedLegs]
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
    if (state.selectedLegs.length) {
      showToast('Entry cleared.', 'info');
    }
    dispatch({ type: 'clearEntry' });
  }, [showToast, state.selectedLegs.length]);

  const value = useMemo<EntryBuilderContextValue>(
    () => ({
      legs: state.selectedLegs,
      selectedLegs: state.selectedLegs,
      amount: state.entryAmount,
      entryAmount: state.entryAmount,
      selectedEntryType: state.selectedEntryType,
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
      setSelectedEntryType: (entryType: string) => dispatch({ type: 'setSelectedEntryType', entryType }),
      openEntryBuilder: () => dispatch({ type: 'openEntryBuilder' }),
      closeEntryBuilder: () => dispatch({ type: 'closeEntryBuilder' }),
      setShowAllCandidates: (nextValue: boolean) => dispatch({ type: 'setShowAllCandidates', value: nextValue }),
      toggleShowAllCandidates: () => dispatch({ type: 'toggleShowAllCandidates' }),
      isSelected: (legId: string) => state.selectedLegs.some((leg) => leg.id === legId),
      isCompatible: (market: MarketOpportunity) => !lockedProviderKey || market.entryCompatibilityGroup === lockedProviderKey
    }),
    [addLeg, clearEntry, lockedProviderKey, lockedProviderName, removeLeg, state.entryAmount, state.isDrawerOpen, state.selectedEntryType, state.selectedLegs, state.showAllCandidates, toggleLeg]
  );

  useEffect(() => {
    persistEntryBuilderState(state);
  }, [state]);

  useEffect(() => {
    const controller = new AbortController();
    getEntryRules(controller.signal)
      .then((nextRules) => {
        if (nextRules.length) {
          setRules(nextRules);
        }
      })
      .catch(() => {
        setRules(mockEntryRules);
      });

    return () => controller.abort();
  }, []);

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
        selectedEntryType: '',
        isDrawerOpen: false,
        showAllCandidates: false
      };
    case 'setEntryAmount':
      return { ...state, entryAmount: action.amount };
    case 'setSelectedEntryType':
      return { ...state, selectedEntryType: action.entryType };
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

function loadPersistedEntryBuilderState(): EntryBuilderState {
  if (typeof window === 'undefined') return initialState;

  try {
    const raw = window.localStorage.getItem(ENTRY_BUILDER_STORAGE_KEY);
    if (!raw) return initialState;

    const parsed = JSON.parse(raw) as Partial<EntryBuilderState> & { version?: number };
    if (parsed.version !== ENTRY_BUILDER_STORAGE_VERSION || !Array.isArray(parsed.selectedLegs)) {
      return initialState;
    }

    return {
      selectedLegs: parsed.selectedLegs.filter(isPersistableEntryLeg),
      entryAmount: typeof parsed.entryAmount === 'number' && Number.isFinite(parsed.entryAmount) && parsed.entryAmount > 0 ? parsed.entryAmount : initialState.entryAmount,
      selectedEntryType: typeof parsed.selectedEntryType === 'string' ? parsed.selectedEntryType : initialState.selectedEntryType,
      isDrawerOpen: typeof parsed.isDrawerOpen === 'boolean' ? parsed.isDrawerOpen : initialState.isDrawerOpen,
      showAllCandidates: typeof parsed.showAllCandidates === 'boolean' ? parsed.showAllCandidates : initialState.showAllCandidates
    };
  } catch {
    return initialState;
  }
}

function persistEntryBuilderState(state: EntryBuilderState) {
  if (typeof window === 'undefined') return;

  try {
    if (!state.selectedLegs.length) {
      window.localStorage.removeItem(ENTRY_BUILDER_STORAGE_KEY);
      return;
    }

    window.localStorage.setItem(
      ENTRY_BUILDER_STORAGE_KEY,
      JSON.stringify({
        version: ENTRY_BUILDER_STORAGE_VERSION,
        selectedLegs: state.selectedLegs,
        entryAmount: state.entryAmount,
        selectedEntryType: state.selectedEntryType,
        isDrawerOpen: state.isDrawerOpen,
        showAllCandidates: state.showAllCandidates
      })
    );
  } catch {
    // Storage can fail in private modes or when quota is unavailable; keep the in-memory slip working.
  }
}

function isPersistableEntryLeg(value: unknown): value is EntryLeg {
  if (!value || typeof value !== 'object') return false;
  const leg = value as Partial<EntryLeg>;
  return typeof leg.id === 'string' && typeof leg.entryCompatibilityGroup === 'string' && typeof leg.entryProvider === 'string';
}
