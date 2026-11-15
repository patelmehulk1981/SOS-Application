import { authApi, meApi } from '@/api/endpoints';
import { ApiError, NetworkError, SessionExpiredError } from '@/api/errors';
import { tokenManager } from '@/api/index';
import { queryClient } from '@/api/queryClient';
import type { AuthResponse, LoginConsent, SessionUser } from '@/api/types';
import { getDeviceId } from '@/lib/device';
import { pushInstallation } from '@/lib/pushInstallation';
import { StorageKeys, secureStorage } from '@/lib/secureStorage';
import { phoneAuth } from './phoneAuth';
import { useSessionStore } from './sessionStore';

/**
 * Session lifecycle. Screens call these functions; route guards react to useSessionStore.
 */

/** App start: resume a stored session, staying usable offline with the cached user summary. */
export async function bootstrapSession(): Promise<void> {
  if (!(await tokenManager.hasStoredSession())) {
    useSessionStore.setState({ status: 'signedOut', user: null });
    return;
  }

  const cachedUser = await readCachedUser();
  if (cachedUser) {
    // Show the app immediately (an SOS app must open without waiting for the network) and
    // validate the session in the background. A rejected refresh signs the user out.
    useSessionStore.setState({ status: 'signedIn', user: cachedUser, endedReason: null });
    tokenManager.refresh().catch(() => {
      // SessionExpiredError: onSessionEnded already switched to signedOut.
      // NetworkError / 5xx: keep the session; the next request retries the refresh.
    });
    return;
  }

  try {
    await tokenManager.refresh(); // onSessionUpdated sets signedIn
  } catch {
    if (useSessionStore.getState().status === 'loading') {
      useSessionStore.setState({ status: 'signedOut', user: null });
    }
  }
}

export interface LoginOptions {
  inviteCode?: string;
  consent?: LoginConsent;
}

/**
 * Exchanges the verified phone (Firebase ID token) for an API session.
 * Throws ApiError `auth.registration_required` for a new number without an invite: the caller
 * then collects the invite code and consent and calls again.
 */
export async function loginWithVerifiedPhone(options: LoginOptions = {}): Promise<AuthResponse> {
  const [firebaseIdToken, deviceId] = await Promise.all([phoneAuth.getIdToken(), getDeviceId()]);
  let auth: AuthResponse;
  try {
    auth = await authApi.login({ firebaseIdToken, deviceId, ...options });
  } catch (error) {
    if (error instanceof ApiError && error.code === 'duplicate') {
      // Two registrations for this number raced; the other one won, so this is now a plain login.
      auth = await authApi.login({ firebaseIdToken, deviceId });
    } else {
      throw error;
    }
  }

  queryClient.clear();
  await tokenManager.startSession(auth);
  await phoneAuth.signOut();
  return auth;
}

/** Accepts the current Privacy Notice / Terms (403 policy.acceptance_required flow). */
export async function acceptCurrentPolicy(policyVersion: string): Promise<void> {
  const result = await meApi.acceptPolicy(policyVersion);
  tokenManager.replaceAccessToken(result.accessToken, result.accessTokenExpiresAt);
  await updateUser((user) => ({ ...user, policyAcceptanceRequired: false }));
}

/** Applies a profile change to the cached session user (e.g. after profile setup). */
export async function updateUser(change: (user: SessionUser) => SessionUser): Promise<void> {
  const current = useSessionStore.getState().user;
  if (!current) return;
  const next = change(current);
  useSessionStore.setState({ user: next });
  await secureStorage.set(StorageKeys.sessionUser, JSON.stringify(next));
}

type Cleanup = () => Promise<void> | void;
const signOutCleanups = new Set<Cleanup>();

/**
 * Features register device data that must not outlive the session (e.g. an undelivered SOS),
 * without the auth layer importing feature code.
 */
export function registerSignOutCleanup(cleanup: Cleanup): () => void {
  signOutCleanups.add(cleanup);
  return () => signOutCleanups.delete(cleanup);
}

// Runs on every way a session ends: our sign-out, and the server ending it (revoked, suspended, deleted).
useSessionStore.subscribe((state, previous) => {
  if (previous.status === 'signedIn' && state.status === 'signedOut') {
    void Promise.allSettled([...signOutCleanups].map(async (cleanup) => cleanup()));
  }
});

/**
 * Signs out: revokes the session on the server when reachable, then always forgets it locally.
 * `reason` is shown once on the welcome screen (e.g. after account deletion).
 */
export async function signOut(reason: string | null = null, options: { skipServer?: boolean } = {}): Promise<void> {
  // skipServer: the server already ended every session (account deletion), so there is nothing to revoke.
  if (!options.skipServer && (await tokenManager.hasStoredSession())) {
    try {
      // Obtain a usable access token first: if that rotates the refresh token, the logout body
      // must carry the new one (sending the rotated one would look like token theft to the API).
      await tokenManager.getAccessToken();
      const refreshToken = await tokenManager.getStoredRefreshToken();
      // The installation id stops pushes to this phone for this account (another account may sign in next).
      if (refreshToken) await authApi.logout(refreshToken, pushInstallation.get() ?? undefined);
    } catch (error) {
      // Offline or already revoked: the local sign-out below still protects this device.
      if (!(error instanceof NetworkError || error instanceof ApiError || error instanceof SessionExpiredError)) {
        throw error;
      }
    }
  }
  await tokenManager.clear();
  await phoneAuth.signOut();
  queryClient.clear();
  useSessionStore.setState({ status: 'signedOut', user: null, endedReason: reason });
}

async function readCachedUser(): Promise<SessionUser | null> {
  const raw = await secureStorage.get(StorageKeys.sessionUser);
  if (!raw) return null;
  try {
    const parsed = JSON.parse(raw) as Partial<SessionUser>;
    return typeof parsed.id === 'string' && typeof parsed.role === 'string' ? (parsed as SessionUser) : null;
  } catch {
    return null;
  }
}
