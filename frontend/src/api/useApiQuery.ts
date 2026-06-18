import { useEffect, useState } from 'react';

export interface ApiQueryState<T> {
  data?: T;
  loading: boolean;
  error?: Error;
}

export function useApiQuery<T>(loader: (signal: AbortSignal) => Promise<T>, deps: readonly unknown[] = []): ApiQueryState<T> {
  const [state, setState] = useState<ApiQueryState<T>>({ loading: true });

  useEffect(() => {
    const controller = new AbortController();
    setState((current) => ({ data: current.data, loading: true }));

    loader(controller.signal)
      .then((data) => {
        if (!controller.signal.aborted) {
          setState({ data, loading: false });
        }
      })
      .catch((error: Error) => {
        if (!controller.signal.aborted) {
          setState({ error, loading: false });
        }
      });

    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);

  return state;
}
