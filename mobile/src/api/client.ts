import { ApiError, NetworkError } from './errors';
import type { ProblemDetails } from './types';

export type QueryValue = string | number | boolean | null | undefined;

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
  /** JSON body. */
  body?: unknown;
  /** Multipart body (photos). Takes precedence over `body`. */
  form?: FormData;
  query?: Record<string, QueryValue>;
  /** Send the bearer token (default true). Anonymous endpoints pass false. */
  auth?: boolean;
  signal?: AbortSignal;
  timeoutMs?: number;
  /** Extra request headers (e.g. X-Location-Key for background location). */
  headers?: Record<string, string>;
}

export interface AccessTokenSource {
  getAccessToken(): Promise<string>;
  getTokenAfterUnauthorized(rejectedToken: string): Promise<string>;
}

export interface ApiClientDeps {
  baseUrl: string;
  userAgent: string;
  tokens: AccessTokenSource;
  /** Called on 403 policy.acceptance_required: the app must show the updated notice. */
  onPolicyAcceptanceRequired?: () => void;
  fetch?: typeof fetch;
  newCorrelationId?: () => string;
}

const DEFAULT_TIMEOUT_MS = 20_000;
/** Photo uploads on slow mobile networks. */
const UPLOAD_TIMEOUT_MS = 120_000;

export class ApiClient {
  private readonly fetchImpl: typeof fetch;
  private readonly newCorrelationId: () => string;

  constructor(private readonly deps: ApiClientDeps) {
    this.fetchImpl = deps.fetch ?? ((input, init) => fetch(input, init));
    this.newCorrelationId = deps.newCorrelationId ?? defaultCorrelationId;
  }

  get<T>(path: string, options: Omit<RequestOptions, 'method' | 'body' | 'form'> = {}): Promise<T> {
    return this.request<T>(path, { ...options, method: 'GET' });
  }

  post<T>(path: string, body?: unknown, options: Omit<RequestOptions, 'method' | 'body'> = {}): Promise<T> {
    return this.request<T>(path, { ...options, method: 'POST', body });
  }

  put<T>(path: string, body?: unknown, options: Omit<RequestOptions, 'method' | 'body'> = {}): Promise<T> {
    return this.request<T>(path, { ...options, method: 'PUT', body });
  }

  delete<T>(path: string, options: Omit<RequestOptions, 'method' | 'body' | 'form'> = {}): Promise<T> {
    return this.request<T>(path, { ...options, method: 'DELETE' });
  }

  async request<T>(path: string, options: RequestOptions = {}): Promise<T> {
    const useAuth = options.auth ?? true;
    let token = useAuth ? await this.deps.tokens.getAccessToken() : undefined;
    let response = await this.send(path, options, token);

    if (response.status === 401 && token !== undefined) {
      // Expired or revoked between our check and the server's: refresh once and retry once.
      token = await this.deps.tokens.getTokenAfterUnauthorized(token);
      response = await this.send(path, options, token);
    }

    if (!response.ok) {
      const error = await toApiError(response);
      if (error.status === 403 && error.code === 'policy.acceptance_required') {
        this.deps.onPolicyAcceptanceRequired?.();
      }
      throw error;
    }

    if (response.status === 204 || response.headers.get('content-length') === '0') {
      return undefined as T;
    }
    const text = await response.text();
    return (text ? JSON.parse(text) : undefined) as T;
  }

  private async send(path: string, options: RequestOptions, token: string | undefined): Promise<Response> {
    const headers: Record<string, string> = {
      ...options.headers,
      Accept: 'application/json',
      'User-Agent': this.deps.userAgent,
      'X-Correlation-ID': this.newCorrelationId(),
    };
    if (token !== undefined) headers.Authorization = `Bearer ${token}`;

    let body: BodyInit | undefined;
    if (options.form) {
      body = options.form; // fetch sets the multipart boundary itself
    } else if (options.body !== undefined) {
      headers['Content-Type'] = 'application/json';
      body = JSON.stringify(options.body);
    }

    const timeoutMs = options.timeoutMs ?? (options.form ? UPLOAD_TIMEOUT_MS : DEFAULT_TIMEOUT_MS);
    const controller = new AbortController();
    let timedOut = false;
    const timer = setTimeout(() => {
      timedOut = true;
      controller.abort();
    }, timeoutMs);
    const onExternalAbort = () => controller.abort();
    options.signal?.addEventListener('abort', onExternalAbort);

    try {
      return await this.fetchImpl(buildUrl(this.deps.baseUrl, path, options.query), {
        method: options.method ?? 'GET',
        headers,
        body,
        signal: controller.signal,
      });
    } catch (cause) {
      if (options.signal?.aborted) throw cause; // the caller cancelled: not a network failure
      throw new NetworkError(timedOut ? 'The server took too long to respond.' : 'No connection to the server.', timedOut, {
        cause,
      });
    } finally {
      clearTimeout(timer);
      options.signal?.removeEventListener('abort', onExternalAbort);
    }
  }
}

export function buildUrl(baseUrl: string, path: string, query?: Record<string, QueryValue>): string {
  let url = `${baseUrl}/api/v1${path.startsWith('/') ? path : `/${path}`}`;
  if (query) {
    const parts = Object.entries(query)
      .filter(([, value]) => value !== undefined && value !== null && value !== '')
      .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`);
    if (parts.length > 0) url += `?${parts.join('&')}`;
  }
  return url;
}

async function toApiError(response: Response): Promise<ApiError> {
  let problem: ProblemDetails = { status: response.status };
  try {
    const text = await response.text();
    if (text) {
      const parsed: unknown = JSON.parse(text);
      if (parsed && typeof parsed === 'object') problem = parsed as ProblemDetails;
    }
  } catch {
    // Not JSON (e.g. a proxy error page): keep the bare status.
  }
  problem.correlationId ??= response.headers.get('X-Correlation-ID') ?? undefined;

  const retryAfter = Number(response.headers.get('Retry-After'));
  return new ApiError(response.status, problem, Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter : undefined);
}

/** Short id quoted in support requests; the API accepts [A-Za-z0-9._-]{1,64}. */
function defaultCorrelationId(): string {
  const random = Math.random().toString(36).slice(2, 10);
  return `m-${Date.now().toString(36)}-${random}`;
}
