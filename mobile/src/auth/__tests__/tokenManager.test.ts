import { ApiError, NetworkError, SessionExpiredError } from '@/api/errors';
import type { AuthResponse, SessionUser } from '@/api/types';
import { StorageKeys, type KeyValueStore, type StorageKey } from '@/lib/secureStorage';
import { REFRESH_SKEW_MS, TokenManager } from '../tokenManager';

const NOW = Date.parse('2026-11-15T10:00:00Z');

function memoryStore(initial: Partial<Record<StorageKey, string>> = {}): KeyValueStore & { data: Map<string, string> } {
  const data = new Map<string, string>(Object.entries(initial));
  return {
    data,
    get: async (key) => data.get(key) ?? null,
    set: async (key, value) => void data.set(key, value),
    remove: async (key) => void data.delete(key),
  };
}

const user: SessionUser = { id: 'u1', role: 'User', profileCompleted: true, policyAcceptanceRequired: false };

function authResponse(n: number, expiresInMs = 15 * 60_000): AuthResponse {
  return {
    accessToken: `access-${n}`,
    accessTokenExpiresAt: new Date(NOW + expiresInMs).toISOString(),
    refreshToken: `refresh-${n}`,
    refreshTokenExpiresAt: new Date(NOW + 30 * 86_400_000).toISOString(),
    isNewUser: false,
    user,
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

function setup(storeInit: Partial<Record<StorageKey, string>> = { [StorageKeys.refreshToken]: 'refresh-0' }) {
  const storage = memoryStore(storeInit);
  const refresh = jest.fn<Promise<AuthResponse>, [string]>();
  const onSessionUpdated = jest.fn();
  const onSessionEnded = jest.fn();
  const manager = new TokenManager({ storage, refresh, now: () => NOW, onSessionUpdated, onSessionEnded });
  return { storage, refresh, onSessionUpdated, onSessionEnded, manager };
}

describe('TokenManager', () => {
  it('shares one refresh between concurrent callers (rotated tokens must never be replayed)', async () => {
    const { manager, refresh, storage } = setup();
    const pending = deferred<AuthResponse>();
    refresh.mockReturnValueOnce(pending.promise);

    const calls = [manager.getAccessToken(), manager.getAccessToken(), manager.getAccessToken()];
    await Promise.resolve(); // let the storage read happen
    await Promise.resolve();
    pending.resolve(authResponse(1));

    await expect(Promise.all(calls)).resolves.toEqual(['access-1', 'access-1', 'access-1']);
    expect(refresh).toHaveBeenCalledTimes(1);
    expect(refresh).toHaveBeenCalledWith('refresh-0');
    expect(storage.data.get(StorageKeys.refreshToken)).toBe('refresh-1');
    expect(JSON.parse(storage.data.get(StorageKeys.sessionUser)!)).toEqual(user);
  });

  it('reuses a valid access token and refreshes inside the expiry skew', async () => {
    const { manager, refresh } = setup();
    await manager.startSession(authResponse(1, REFRESH_SKEW_MS + 5_000));
    await expect(manager.getAccessToken()).resolves.toBe('access-1');
    expect(refresh).not.toHaveBeenCalled();

    await manager.startSession(authResponse(2, REFRESH_SKEW_MS - 1));
    refresh.mockResolvedValueOnce(authResponse(3));
    await expect(manager.getAccessToken()).resolves.toBe('access-3');
    expect(refresh).toHaveBeenCalledWith('refresh-2');
  });

  it('ends the session on a 401 from /auth/refresh and reports the server code', async () => {
    const { manager, refresh, storage, onSessionEnded } = setup();
    refresh.mockRejectedValueOnce(new ApiError(401, { code: 'auth.refresh_reused' }));

    await expect(manager.getAccessToken()).rejects.toBeInstanceOf(SessionExpiredError);
    expect(onSessionEnded).toHaveBeenCalledWith('auth.refresh_reused');
    expect(storage.data.has(StorageKeys.refreshToken)).toBe(false);
  });

  it('keeps the stored session when the refresh fails for network reasons', async () => {
    const { manager, refresh, storage, onSessionEnded } = setup();
    refresh.mockRejectedValueOnce(new NetworkError('offline', false));

    await expect(manager.getAccessToken()).rejects.toBeInstanceOf(NetworkError);
    expect(onSessionEnded).not.toHaveBeenCalled();
    expect(storage.data.get(StorageKeys.refreshToken)).toBe('refresh-0');

    refresh.mockResolvedValueOnce(authResponse(1));
    await expect(manager.getAccessToken()).resolves.toBe('access-1');
  });

  it('keeps the stored session on a 5xx or 429 from /auth/refresh', async () => {
    const { manager, refresh, onSessionEnded } = setup();
    refresh.mockRejectedValueOnce(new ApiError(503, {}));
    await expect(manager.getAccessToken()).rejects.toBeInstanceOf(ApiError);
    refresh.mockRejectedValueOnce(new ApiError(429, { code: 'rate_limited' }));
    await expect(manager.getAccessToken()).rejects.toBeInstanceOf(ApiError);
    expect(onSessionEnded).not.toHaveBeenCalled();
  });

  it('fails fast without calling the API when no refresh token is stored', async () => {
    const { manager, refresh, onSessionEnded } = setup({});
    await expect(manager.getAccessToken()).rejects.toBeInstanceOf(SessionExpiredError);
    expect(refresh).not.toHaveBeenCalled();
    expect(onSessionEnded).toHaveBeenCalledWith('no_session');
  });

  it('after a 401, reuses a token another caller already refreshed instead of rotating again', async () => {
    const { manager, refresh } = setup();
    await manager.startSession(authResponse(1));
    refresh.mockResolvedValueOnce(authResponse(2));

    // Caller A got a 401 with access-1 → refreshes to access-2.
    await expect(manager.getTokenAfterUnauthorized('access-1')).resolves.toBe('access-2');
    // Caller B also got a 401 with access-1, slightly later → must not refresh again.
    await expect(manager.getTokenAfterUnauthorized('access-1')).resolves.toBe('access-2');
    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it('discards a refresh that completes after sign-out', async () => {
    const { manager, refresh, storage, onSessionUpdated } = setup();
    const pending = deferred<AuthResponse>();
    refresh.mockReturnValueOnce(pending.promise);

    const call = manager.getAccessToken();
    await Promise.resolve();
    await Promise.resolve();
    await manager.clear();
    pending.resolve(authResponse(1));

    await expect(call).rejects.toBeInstanceOf(SessionExpiredError);
    expect(storage.data.has(StorageKeys.refreshToken)).toBe(false);
    expect(onSessionUpdated).not.toHaveBeenCalled();
  });

  it('replaceAccessToken keeps the refresh token (policy acceptance)', async () => {
    const { manager, storage, refresh } = setup();
    await manager.startSession(authResponse(1));
    manager.replaceAccessToken('access-policy', new Date(NOW + 15 * 60_000).toISOString());

    await expect(manager.getAccessToken()).resolves.toBe('access-policy');
    expect(storage.data.get(StorageKeys.refreshToken)).toBe('refresh-1');
    expect(refresh).not.toHaveBeenCalled();
  });
});
