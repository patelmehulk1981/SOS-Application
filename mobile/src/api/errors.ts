import type { ProblemDetails } from './types';

/** A non-2xx response from the API, carrying the stable problem `code` to branch on. */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string;
  readonly detail?: string;
  readonly correlationId?: string;
  readonly fieldErrors: Record<string, string[]>;
  readonly retryAfterSeconds?: number;
  readonly problem: ProblemDetails;

  constructor(status: number, problem: ProblemDetails, retryAfterSeconds?: number) {
    super(problem.detail ?? problem.title ?? `Request failed (${status})`);
    this.name = 'ApiError';
    this.status = status;
    this.code = problem.code ?? fallbackCode(status);
    this.detail = problem.detail;
    this.correlationId = problem.correlationId;
    this.fieldErrors = problem.errors ?? {};
    this.retryAfterSeconds = retryAfterSeconds;
    this.problem = problem;
  }
}

/** The request never got an HTTP response: offline, DNS, TLS, or timeout. */
export class NetworkError extends Error {
  readonly timedOut: boolean;

  constructor(message: string, timedOut: boolean, options?: { cause?: unknown }) {
    super(message, options);
    this.name = 'NetworkError';
    this.timedOut = timedOut;
  }
}

/** The stored session can no longer be refreshed; the user has to sign in again. */
export class SessionExpiredError extends Error {
  readonly reason: string;

  constructor(reason: string) {
    super('Your session has ended. Please sign in again.');
    this.name = 'SessionExpiredError';
    this.reason = reason;
  }
}

export function isApiError(error: unknown, code?: string): error is ApiError {
  return error instanceof ApiError && (code === undefined || error.code === code);
}

function fallbackCode(status: number): string {
  switch (status) {
    case 400:
      return 'bad_request';
    case 401:
      return 'unauthenticated';
    case 403:
      return 'forbidden';
    case 404:
      return 'not_found';
    case 409:
      return 'conflict';
    case 429:
      return 'rate_limited';
    default:
      return status >= 500 ? 'unexpected_error' : 'request_failed';
  }
}
