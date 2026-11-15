import { useEffect } from 'react';
import { AppState } from 'react-native';

import { meApi } from '@/api/endpoints';
import { registerSignOutCleanup } from '@/auth/session';

import { getOneFix } from './useCurrentLocation';

/**
 * Keeps the last-known location fresh while the app is in use, for people who share their location:
 * neighbours are alerted only if their location is under 12 hours old (docs/05). At most one update per
 * 15 minutes, only with permission already granted (never prompts), foreground only. Background updates,
 * when turned on, are in ./background (docs/06).
 */
export const HEARTBEAT_INTERVAL_MS = 15 * 60_000;

export function shouldSendHeartbeat(lastSentAt: number | null, now: number): boolean {
  return lastSentAt === null || now - lastSentAt >= HEARTBEAT_INTERVAL_MS || now < lastSentAt;
}

let lastSentAt: number | null = null;
let inFlight = false;

export async function sendHeartbeat(now: number = Date.now()): Promise<boolean> {
  if (inFlight || !shouldSendHeartbeat(lastSentAt, now)) return false;
  inFlight = true;
  try {
    const fix = await getOneFix();
    if (!fix) return false;
    await meApi.updateLocation(fix);
    lastSentAt = now;
    return true;
  } catch {
    return false; // offline, consent withdrawn elsewhere (422), stale fix: try again next time
  } finally {
    inFlight = false;
  }
}

/** Sends on mount and whenever the app returns to the foreground, while `enabled` (LocationSharing consent). */
export function useLocationHeartbeat(enabled: boolean): void {
  useEffect(() => {
    if (!enabled) return;
    void sendHeartbeat();
    const subscription = AppState.addEventListener('change', (state) => {
      if (state === 'active') void sendHeartbeat();
    });
    return () => subscription.remove();
  }, [enabled]);
}

/** For tests and sign-out. */
export function resetHeartbeat(): void {
  lastSentAt = null;
}

// The next account on this phone sends its own location straight away.
registerSignOutCleanup(resetHeartbeat);
