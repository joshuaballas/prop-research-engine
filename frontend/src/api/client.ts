import { getAccessToken } from './authToken';

export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5299';

export class ApiError extends Error {
  status: number;
  details: unknown;

  constructor(message: string, status: number, details: unknown) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.details = details;
  }
}

export async function apiGet<T>(path: string, signal?: AbortSignal): Promise<T> {
  const token = await getAccessToken();
  const response = await fetch(`${API_BASE_URL}${path}`, {
    method: 'GET',
    signal,
    headers: {
      Accept: 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {})
    }
  });

  const contentType = response.headers.get('content-type') ?? '';
  const payload = contentType.includes('application/json') ? await response.json() : await response.text();

  if (!response.ok) {
    const message =
      typeof payload === 'object' && payload && 'error' in payload
        ? String((payload as { error?: unknown }).error)
        : `API request failed with ${response.status}`;
    throw new ApiError(message, response.status, payload);
  }

  return payload as T;
}

export function buildQuery(params: Record<string, string | number | boolean | undefined | null>) {
  const search = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== '') {
      search.set(key, String(value));
    }
  });

  const query = search.toString();
  return query ? `?${query}` : '';
}
