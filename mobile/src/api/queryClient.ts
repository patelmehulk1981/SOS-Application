import { QueryClient } from '@tanstack/react-query';

import { ApiError, SessionExpiredError } from './errors';

/** Retrying cannot fix a 4xx or an ended session; network errors and 5xx get two more tries. */
export function shouldRetry(failureCount: number, error: unknown): boolean {
  if (error instanceof SessionExpiredError) return false;
  if (error instanceof ApiError && error.status < 500 && error.status !== 429) return false;
  return failureCount < 2;
}

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: shouldRetry,
      staleTime: 30_000,
    },
    mutations: {
      retry: false,
    },
  },
});
