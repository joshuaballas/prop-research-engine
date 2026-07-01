import { useEffect, useState } from 'react';

export interface ApiQueryState<T> {
  data?: T;
  loading: boolean;
  error?: Error;
}

export interface ApiQueryOptions {
  refetchIntervalMs?: number;
  refetchOnWindowFocus?: boolean;
}

export function useApiQuery<T>(
  loader: (signal: AbortSignal) => Promise<T>,
  deps: readonly unknown[] = [],
  options: ApiQueryOptions = {}
): ApiQueryState<T> {
  const [state, setState] = useState<ApiQueryState<T>>({ loading: true });
  const [pollTick, setPollTick] = useState(0);
  const [focusTick, setFocusTick] = useState(0);

  useEffect(() => {
    if (!options.refetchIntervalMs || options.refetchIntervalMs <= 0) {
      return;
    }

    const timer = window.setInterval(() => {
      setPollTick((value) => value + 1);
    }, options.refetchIntervalMs);

    return () => {
      window.clearInterval(timer);
    };
  }, [options.refetchIntervalMs]);

  useEffect(() => {
    if (!options.refetchOnWindowFocus) {
      return;
    }

    const onFocus = () => {
      setFocusTick((value) => value + 1);
    };

    window.addEventListener('focus', onFocus);
    return () => {
      window.removeEventListener('focus', onFocus);
    };
  }, [options.refetchOnWindowFocus]);

  useEffect(() => {
    const controller = new AbortController();
    setState((current) => ({ data: current.data, loading: current.data === undefined }));

    loader(controller.signal)
      .then((data) => {
        if (!controller.signal.aborted) {
          setState({ data, loading: false });
        }
      })
      .catch((error: Error) => {
        if (!controller.signal.aborted) {
          setState((current) => ({ data: current.data, error, loading: false }));
        }
      });

    return () => controller.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, pollTick, focusTick]);

  return state;
}
