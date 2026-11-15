import { TokenManager } from '@/auth/tokenManager';
import { useSessionStore } from '@/auth/sessionStore';
import { env } from '@/config/env';
import { getDeviceId, userAgent } from '@/lib/device';
import { secureStorage } from '@/lib/secureStorage';
import { ApiClient } from './client';
import type { AuthResponse } from './types';

/**
 * App-wide API singletons. The refresh call uses an unauthenticated client so it never
 * recurses into the token manager.
 */
export const anonymousClient = new ApiClient({
  baseUrl: env.apiBaseUrl,
  userAgent,
  tokens: {
    getAccessToken: () => Promise.reject(new Error('Anonymous client has no session.')),
    getTokenAfterUnauthorized: () => Promise.reject(new Error('Anonymous client has no session.')),
  },
});

export const tokenManager = new TokenManager({
  storage: secureStorage,
  refresh: async (refreshToken) =>
    anonymousClient.post<AuthResponse>('/auth/refresh', { refreshToken, deviceId: await getDeviceId() }, { auth: false }),
  onSessionUpdated: (user) => useSessionStore.setState({ status: 'signedIn', user, endedReason: null }),
  onSessionEnded: (reason) => useSessionStore.setState({ status: 'signedOut', user: null, endedReason: reason }),
});

export const api = new ApiClient({
  baseUrl: env.apiBaseUrl,
  userAgent,
  tokens: tokenManager,
  onPolicyAcceptanceRequired: () =>
    useSessionStore.setState((state) =>
      state.user ? { user: { ...state.user, policyAcceptanceRequired: true } } : state,
    ),
});
