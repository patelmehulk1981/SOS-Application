import { ApiError, SessionExpiredError } from '@/api/errors';
import type { AuthResponse, SessionUser } from '@/api/types';
import { StorageKeys, type KeyValueStore } from '@/lib/secureStorage';

/** Refresh this long before the access token expires, so a request never races the expiry. */
export const REFRESH_SKEW_MS = 60_000;

export interface TokenManagerDeps {
  storage: KeyValueStore;
  /** POST /auth/refresh without authentication (must not go through the token manager). */
  refresh: (refreshToken: string) => Promise<AuthResponse>;
  now?: () => number;
  /** A login or refresh produced a new session (user summary may have changed, e.g. role or policy flag). */
  onSessionUpdated: (user: SessionUser) => void;
  /** The session cannot continue (revoked, expired, reused token, suspended account). */
  onSessionEnded: (reason: string) => void;
}

interface AccessToken {
  token: string;
  expiresAtMs: number;
}

/**
 * Owns the API session tokens (docs/03b-auth-profile.md, "Token handling"):
 * - the access token lives in memory only;
 * - the refresh token lives in secure storage and rotates on every refresh;
 * - refreshes are single-flight: the server treats a second use of a rotated refresh token as theft
 *   and revokes the whole session family, so concurrent callers must share one refresh.
 */
export class TokenManager {
  private access: AccessToken | null = null;
  private inFlight: Promise<string> | null = null;
  /** Bumped on sign-in/sign-out so a refresh that finishes after sign-out cannot resurrect the session. */
  private generation = 0;
  private readonly now: () => number;

  constructor(private readonly deps: TokenManagerDeps) {
    this.now = deps.now ?? Date.now;
  }

  /** Stores a fresh session from POST /auth/login. */
  async startSession(auth: AuthResponse): Promise<void> {
    this.generation++;
    this.inFlight = null;
    await this.persist(auth);
  }

  /** Replaces only the access token (POST /me/policy-acceptance returns one with the new policy claim). */
  replaceAccessToken(token: string, expiresAt: string): void {
    this.access = { token, expiresAtMs: Date.parse(expiresAt) };
  }

  async hasStoredSession(): Promise<boolean> {
    return (await this.deps.storage.get(StorageKeys.refreshToken)) !== null;
  }

  async getStoredRefreshToken(): Promise<string | null> {
    return this.deps.storage.get(StorageKeys.refreshToken);
  }

  /** A valid access token, refreshing first when it is missing or about to expire. */
  async getAccessToken(): Promise<string> {
    const current = this.access;
    if (current && current.expiresAtMs - this.now() > REFRESH_SKEW_MS) {
      return current.token;
    }
    return this.refresh();
  }

  /**
   * Called after a 401 on a request sent with `rejectedToken`. If another caller already replaced it,
   * the new token is reused instead of refreshing again.
   */
  async getTokenAfterUnauthorized(rejectedToken: string): Promise<string> {
    const current = this.access;
    if (current && current.token !== rejectedToken && current.expiresAtMs - this.now() > REFRESH_SKEW_MS) {
      return current.token;
    }
    this.access = null;
    return this.refresh();
  }

  /** Forgets the session locally (call after POST /auth/logout, or when the server ended it). */
  async clear(): Promise<void> {
    this.generation++;
    this.inFlight = null;
    this.access = null;
    await Promise.all([
      this.deps.storage.remove(StorageKeys.refreshToken),
      this.deps.storage.remove(StorageKeys.sessionUser),
    ]);
  }

  refresh(): Promise<string> {
    if (!this.inFlight) {
      const generation = this.generation;
      this.inFlight = this.doRefresh(generation).finally(() => {
        if (this.generation === generation) this.inFlight = null;
      });
    }
    return this.inFlight;
  }

  private async doRefresh(generation: number): Promise<string> {
    const refreshToken = await this.deps.storage.get(StorageKeys.refreshToken);
    if (!refreshToken) {
      throw await this.end(generation, 'no_session');
    }

    let auth: AuthResponse;
    try {
      auth = await this.deps.refresh(refreshToken);
    } catch (error) {
      // 401 (invalid, expired, reused, account inactive) means the session is over. Anything else
      // (offline, timeout, 5xx, 429) keeps the stored session so the next attempt can succeed.
      if (error instanceof ApiError && error.status === 401) {
        throw await this.end(generation, error.code);
      }
      throw error;
    }

    if (generation !== this.generation) {
      // Signed out (or in again) while this refresh was running: discard its result.
      throw new SessionExpiredError('superseded');
    }
    await this.persist(auth);
    return auth.accessToken;
  }

  private async persist(auth: AuthResponse): Promise<void> {
    this.access = { token: auth.accessToken, expiresAtMs: Date.parse(auth.accessTokenExpiresAt) };
    await this.deps.storage.set(StorageKeys.refreshToken, auth.refreshToken);
    await this.deps.storage.set(StorageKeys.sessionUser, JSON.stringify(auth.user));
    this.deps.onSessionUpdated(auth.user);
  }

  private async end(generation: number, reason: string): Promise<SessionExpiredError> {
    if (generation === this.generation) {
      await this.clear();
      this.deps.onSessionEnded(reason);
    }
    return new SessionExpiredError(reason);
  }
}
