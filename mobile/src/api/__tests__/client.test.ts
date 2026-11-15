import { ApiClient, buildUrl, type AccessTokenSource } from '../client';
import { ApiError, NetworkError } from '../errors';

function jsonResponse(status: number, body?: unknown, headers: Record<string, string> = {}): Response {
  return new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json', ...headers },
  });
}

function setup(responses: (Response | Error)[], tokens?: Partial<AccessTokenSource>) {
  const fetchMock = jest.fn<Promise<Response>, [RequestInfo | URL, RequestInit | undefined]>();
  for (const r of responses) {
    if (r instanceof Error) fetchMock.mockRejectedValueOnce(r);
    else fetchMock.mockResolvedValueOnce(r);
  }
  const tokenSource: AccessTokenSource = {
    getAccessToken: jest.fn(async () => 'token-1'),
    getTokenAfterUnauthorized: jest.fn(async () => 'token-2'),
    ...tokens,
  };
  const onPolicyAcceptanceRequired = jest.fn();
  const client = new ApiClient({
    baseUrl: 'https://api.test',
    userAgent: 'CommunitySos/1.0.0 (android)',
    tokens: tokenSource,
    onPolicyAcceptanceRequired,
    fetch: fetchMock as unknown as typeof fetch,
    newCorrelationId: () => 'm-test',
  });
  const headersOf = (call: number) => fetchMock.mock.calls[call]![1]!.headers as Record<string, string>;
  return { client, fetchMock, tokenSource, onPolicyAcceptanceRequired, headersOf };
}

describe('buildUrl', () => {
  it('prefixes /api/v1 and drops empty query values', () => {
    expect(buildUrl('https://api.test', '/admin/sos', { page: 2, status: undefined, search: '', q: 'a b' })).toBe(
      'https://api.test/api/v1/admin/sos?page=2&q=a%20b',
    );
  });
});

describe('ApiClient', () => {
  it('sends bearer, user agent, correlation id and JSON body', async () => {
    const { client, fetchMock, headersOf } = setup([jsonResponse(200, { ok: true })]);
    await expect(client.put('/me/preferences', { alertsEnabled: false })).resolves.toEqual({ ok: true });

    const [url, init] = fetchMock.mock.calls[0]!;
    expect(url).toBe('https://api.test/api/v1/me/preferences');
    expect(init!.method).toBe('PUT');
    expect(init!.body).toBe('{"alertsEnabled":false}');
    expect(headersOf(0)).toMatchObject({
      Authorization: 'Bearer token-1',
      'User-Agent': 'CommunitySos/1.0.0 (android)',
      'X-Correlation-ID': 'm-test',
      'Content-Type': 'application/json',
    });
  });

  it('does not touch tokens for anonymous calls', async () => {
    const { client, tokenSource, headersOf } = setup([jsonResponse(200, { policyVersion: '2026-11' })]);
    await client.get('/app-info', { auth: false });
    expect(tokenSource.getAccessToken).not.toHaveBeenCalled();
    expect(headersOf(0).Authorization).toBeUndefined();
  });

  it('refreshes once and retries once after a 401', async () => {
    const { client, fetchMock, tokenSource, headersOf } = setup([jsonResponse(401, { code: 'unauthenticated' }), jsonResponse(200, { id: 'me' })]);
    await expect(client.get('/me')).resolves.toEqual({ id: 'me' });
    expect(tokenSource.getTokenAfterUnauthorized).toHaveBeenCalledWith('token-1');
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(headersOf(1).Authorization).toBe('Bearer token-2');
  });

  it('gives up after the retry also returns 401', async () => {
    const { client, fetchMock } = setup([jsonResponse(401, {}), jsonResponse(401, { code: 'auth.account_inactive' })]);
    await expect(client.get('/me')).rejects.toMatchObject({ status: 401, code: 'auth.account_inactive' });
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('maps problem+json to ApiError with code, detail, field errors and correlation id', async () => {
    const { client } = setup([
      jsonResponse(400, {
        status: 400,
        code: 'validation_failed',
        detail: 'One or more validation errors occurred.',
        correlationId: 'm-test',
        errors: { fullName: ['Full name is required.'] },
      }),
    ]);
    const error = (await client.put('/me/profile', {}).catch((e: unknown) => e)) as ApiError;
    expect(error).toBeInstanceOf(ApiError);
    expect(error.code).toBe('validation_failed');
    expect(error.fieldErrors.fullName).toEqual(['Full name is required.']);
    expect(error.correlationId).toBe('m-test');
  });

  it('signals policy re-acceptance on 403 policy.acceptance_required', async () => {
    const { client, onPolicyAcceptanceRequired } = setup([jsonResponse(403, { code: 'policy.acceptance_required' })]);
    await expect(client.get('/sos/mine')).rejects.toMatchObject({ code: 'policy.acceptance_required' });
    expect(onPolicyAcceptanceRequired).toHaveBeenCalledTimes(1);
  });

  it('reads Retry-After on 429 and falls back to a code for non-JSON errors', async () => {
    const { client } = setup([new Response('<html>busy</html>', { status: 429, headers: { 'Retry-After': '30' } })]);
    await expect(client.post('/auth/login', {}, { auth: false })).rejects.toMatchObject({
      status: 429,
      code: 'rate_limited',
      retryAfterSeconds: 30,
    });
  });

  it('returns undefined for 204 No Content', async () => {
    const { client } = setup([new Response(null, { status: 204 })]);
    await expect(client.post('/me/devices', { fcmToken: 'x' })).resolves.toBeUndefined();
  });

  it('wraps transport failures in NetworkError', async () => {
    const { client } = setup([new TypeError('Network request failed')]);
    await expect(client.get('/me')).rejects.toBeInstanceOf(NetworkError);
  });

  it('times out slow requests', async () => {
    jest.useFakeTimers();
    try {
      const { client, fetchMock } = setup([]);
      fetchMock.mockImplementationOnce(
        (_url, init) =>
          new Promise((_resolve, reject) => {
            init!.signal!.addEventListener('abort', () => reject(new Error('aborted')));
          }),
      );
      const call = client.get('/me', { timeoutMs: 1_000 });
      const assertion = expect(call).rejects.toMatchObject({ name: 'NetworkError', timedOut: true });
      await jest.advanceTimersByTimeAsync(1_000);
      await assertion;
    } finally {
      jest.useRealTimers();
    }
  });
});
