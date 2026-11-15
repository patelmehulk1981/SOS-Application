import { backgroundLocationApi } from '@/api/endpoints';

import type { Fix } from '../fix';
import { recenterGeofence, stopAll } from './native';
import { outcomeOf, shouldReport, type ReportOutcome } from './policy';
import { locationKeyStore, readState, writeState } from './state';

export type ReportResult = ReportOutcome | 'disabled' | 'throttled' | 'noKey';

let inFlight: Promise<ReportResult> | null = null;

/**
 * Sends one background fix with the location key, if it is worth sending (moved, or the keep-alive
 * is due). Runs inside OS-started tasks, often with seconds to spare, so it never touches the session.
 * Concurrent tasks (an updates batch and a geofence exit at once) share one attempt.
 */
export function reportFix(fix: Fix, now: number = Date.now()): Promise<ReportResult> {
  inFlight ??= doReport(fix, now).finally(() => {
    inFlight = null;
  });
  return inFlight;
}

async function doReport(fix: Fix, now: number): Promise<ReportResult> {
  const state = await readState();
  if (!state.enabled || state.needsNewKey) return 'disabled';
  if (!shouldReport(state.lastReport, fix, now)) return 'throttled';

  const key = await locationKeyStore.get().catch(() => null); // unreadable before the first unlock after a reboot
  if (!key) return 'noKey';

  try {
    await backgroundLocationApi.report(key, fix);
  } catch (error) {
    const outcome = outcomeOf(error);
    if (outcome === 'keyInvalid' || outcome === 'consentWithdrawn') await halt(outcome);
    return outcome;
  }

  await writeState({ lastReport: { latitude: fix.latitude, longitude: fix.longitude, capturedAt: fix.capturedAt, sentAt: now } });
  await recenterGeofence(fix);
  return 'sent';
}

/** The key no longer works: stop waking the phone for nothing. Opening the app re-arms it (controller.sync). */
async function halt(reason: 'keyInvalid' | 'consentWithdrawn'): Promise<void> {
  await stopAll();
  await locationKeyStore.clear().catch(() => undefined);
  await writeState(reason === 'consentWithdrawn' ? { enabled: false, needsNewKey: false, lastReport: null } : { needsNewKey: true });
}
