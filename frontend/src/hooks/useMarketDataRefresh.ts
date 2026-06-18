import { useSyncExternalStore } from 'react';

let marketDataRefreshVersion = 0;
const listeners = new Set<() => void>();

export function bumpMarketDataRefreshVersion() {
  marketDataRefreshVersion += 1;
  listeners.forEach((listener) => listener());
}

export function useMarketDataRefreshVersion() {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    () => marketDataRefreshVersion,
    () => marketDataRefreshVersion
  );
}
