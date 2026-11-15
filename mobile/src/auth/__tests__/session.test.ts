import type { AuthResponse } from '@/api/types';

// In-memory secure storage (the real one needs the native keychain).
jest.mock('@/lib/secureStorage', () => {
  const data = new Map<string, string>();
  return {
    StorageKeys: { refreshToken: 'auth.refreshToken', sessionUser: 'auth.sessionUser', deviceId: 'device.installId' },
    secureStorage: {
      get: async (key: string) => data.get(key) ?? null,
      set: async (key: string, value: string) => void data.set(key, value),
      remove: async (key: string) => void data.delete(key),
    },
    __data: data,
  };
});

jest.mock('@/api/endpoints', () => ({
  authApi: { login: jest.fn(), logout: jest.fn() },
  meApi: { acceptPolicy: jest.fn() },
}));

jest.mock('@/lib/device', () => ({ getDeviceId: async () => 'device-1' }));

/* eslint-disable import/first */
import { authApi, meApi } from '@/api/endpoints';
import { ApiError, NetworkError } from '@/api/errors';
import { tokenManager } from '@/api/index';
import { pushInstallation } from '@/lib/pushInstallation';
import { phoneAuth } from '../phoneAuth';
import { acceptCurrentPolicy, bootstrapSession, loginWithVerifiedPhone, registerSignOutCleanup, signOut } from '../session';
import { useSessionStore } from '../sessionStore';
/* eslint-enable import/first */

const storage = (jest.requireMock('@/lib/secureStorage') as { __data: Map<string, string> }).__data;
const login = authApi.login as jest.Mock;
const logout = authApi.logout as jest.Mock;

function auth(n: number, overrides: Partial<AuthResponse['user']> = {}): AuthResponse {
  return {
    accessToken: `access-${n}`,
    accessTokenExpiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
    refreshToken: `refresh-${n}`,
    refreshTokenExpiresAt: new Date(Date.now() + 30 * 86_400_000).toISOString(),
    isNewUser: false,
    user: { id: 'u1', role: 'User', fullName: 'Asha Patel', profileCompleted: true, policyAcceptanceRequired: false, ...overrides },
  };
}

async function verifyPhone() {
  await phoneAuth.sendCode('+919876543210');
  await phoneAuth.confirmCode('123456');
}

beforeEach(async () => {
  jest.clearAllMocks();
  await tokenManager.clear();
  storage.clear();
  useSessionStore.setState({ status: 'loading', user: null, endedReason: null });
});

describe('loginWithVerifiedPhone', () => {
  it('exchanges the verified phone for a session and forgets the Firebase session', async () => {
    await verifyPhone();
    login.mockResolvedValueOnce(auth(1));

    await loginWithVerifiedPhone();

    expect(login).toHaveBeenCalledWith({ firebaseIdToken: 'dev:+919876543210', deviceId: 'device-1' });
    expect(useSessionStore.getState()).toMatchObject({ status: 'signedIn', user: { id: 'u1' } });
    expect(storage.get('auth.refreshToken')).toBe('refresh-1');
    await expect(phoneAuth.getIdToken()).rejects.toThrow(); // Firebase/dev session cleared
  });

  it('passes invite code and consent for a first registration', async () => {
    await verifyPhone();
    login.mockResolvedValueOnce({ ...auth(1, { profileCompleted: false }), isNewUser: true });

    await loginWithVerifiedPhone({ inviteCode: 'K7QX2M9PRA', consent: { policyVersion: '2026-11', confirmedAdult: true } });

    expect(login).toHaveBeenCalledWith(
      expect.objectContaining({ inviteCode: 'K7QX2M9PRA', consent: { policyVersion: '2026-11', confirmedAdult: true } }),
    );
    expect(useSessionStore.getState().user?.profileCompleted).toBe(false);
  });

  it('retries as a plain login when a concurrent registration won (409 duplicate)', async () => {
    await verifyPhone();
    login.mockRejectedValueOnce(new ApiError(409, { code: 'duplicate' })).mockResolvedValueOnce(auth(1));

    await loginWithVerifiedPhone({ inviteCode: 'K7QX2M9PRA', consent: { policyVersion: '2026-11', confirmedAdult: true } });

    expect(login).toHaveBeenLastCalledWith({ firebaseIdToken: 'dev:+919876543210', deviceId: 'device-1' });
    expect(useSessionStore.getState().status).toBe('signedIn');
  });

  it('surfaces registration_required so the app can ask for an invite', async () => {
    await verifyPhone();
    login.mockRejectedValueOnce(new ApiError(422, { code: 'auth.registration_required' }));

    await expect(loginWithVerifiedPhone()).rejects.toMatchObject({ code: 'auth.registration_required' });
    expect(useSessionStore.getState().status).toBe('loading');
    await expect(phoneAuth.getIdToken()).resolves.toBe('dev:+919876543210'); // still verified for the second call
  });
});

describe('signOut', () => {
  it('revokes on the server, then clears the device', async () => {
    await tokenManager.startSession(auth(1));
    logout.mockResolvedValueOnce(undefined);

    await signOut();

    expect(logout).toHaveBeenCalledWith('refresh-1', undefined);
    expect(storage.has('auth.refreshToken')).toBe(false);
    expect(useSessionStore.getState()).toMatchObject({ status: 'signedOut', user: null });
  });

  it('tells the API to stop pushing to this phone (installation id)', async () => {
    await tokenManager.startSession(auth(1));
    logout.mockResolvedValueOnce(undefined);
    pushInstallation.set('fid-abc');
    try {
      await signOut();
      expect(logout).toHaveBeenCalledWith('refresh-1', 'fid-abc');
    } finally {
      pushInstallation.set(null);
    }
  });

  it('still signs out locally when offline', async () => {
    await tokenManager.startSession(auth(1));
    logout.mockRejectedValueOnce(new NetworkError('offline', false));

    await signOut('account.deleted');

    expect(storage.has('auth.refreshToken')).toBe(false);
    expect(useSessionStore.getState()).toMatchObject({ status: 'signedOut', endedReason: 'account.deleted' });
  });
});

describe('sign-out cleanups (e.g. an undelivered SOS is discarded)', () => {
  it('run when the user signs out and when the server ends the session', async () => {
    const cleanup = jest.fn();
    const unregister = registerSignOutCleanup(cleanup);
    try {
      await tokenManager.startSession(auth(1));
      logout.mockResolvedValueOnce(undefined);
      await signOut();
      expect(cleanup).toHaveBeenCalledTimes(1);

      // Server-side end: the refresh is rejected (revoked / suspended).
      await tokenManager.startSession(auth(2));
      const refresh = jest.spyOn(tokenManager, 'refresh');
      useSessionStore.setState({ status: 'signedIn' });
      useSessionStore.setState({ status: 'signedOut', user: null, endedReason: 'auth.account_inactive' });
      expect(cleanup).toHaveBeenCalledTimes(2);
      refresh.mockRestore();
    } finally {
      unregister();
    }
  });
});

describe('bootstrapSession', () => {
  it('starts signed out without a stored session', async () => {
    await bootstrapSession();
    expect(useSessionStore.getState().status).toBe('signedOut');
  });

  it('opens immediately with the cached user while the refresh runs in the background', async () => {
    storage.set('auth.refreshToken', 'refresh-0');
    storage.set('auth.sessionUser', JSON.stringify(auth(0).user));
    const refresh = jest.spyOn(tokenManager, 'refresh').mockRejectedValueOnce(new NetworkError('offline', false));

    await bootstrapSession();

    expect(useSessionStore.getState()).toMatchObject({ status: 'signedIn', user: { id: 'u1' } });
    expect(refresh).toHaveBeenCalled();
    expect(storage.get('auth.refreshToken')).toBe('refresh-0'); // offline does not end the session
    refresh.mockRestore();
  });
});

describe('acceptCurrentPolicy', () => {
  it('swaps in the new access token and clears the policy flag', async () => {
    await tokenManager.startSession(auth(1, { policyAcceptanceRequired: true }));
    (meApi.acceptPolicy as jest.Mock).mockResolvedValueOnce({
      accessToken: 'access-policy',
      accessTokenExpiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
    });

    await acceptCurrentPolicy('2026-11');

    await expect(tokenManager.getAccessToken()).resolves.toBe('access-policy');
    expect(useSessionStore.getState().user?.policyAcceptanceRequired).toBe(false);
    expect(JSON.parse(storage.get('auth.sessionUser')!).policyAcceptanceRequired).toBe(false);
  });
});
