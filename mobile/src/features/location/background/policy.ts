import { ApiError, NetworkError } from '@/api/errors';

import { toFix, type Fix, type RawPosition } from '../fix';

/**
 * Pure rules for background location (docs/06-background-location.md). No native calls here, so
 * every decision is unit-tested.
 */

export const BackgroundTasks = {
  /** OS-batched location updates while moving (expo-location). */
  updates: 'community-sos.location.updates',
  /** Exit from a ~500 m circle around the last report; iOS relaunches a terminated app for this. */
  geofence: 'community-sos.location.geofence',
  /** Periodic check-in (WorkManager / BGTaskScheduler) so someone who stays put does not go stale. */
  checkIn: 'community-sos.location.checkin',
} as const;

/** Report again once the phone has moved this far from the last report. */
export const MIN_MOVE_METERS = 250;
/** Report at least this often while stationary (the API forgets locations after 12 hours). */
export const KEEPALIVE_MS = 60 * 60_000;
/** The API ignores fixes worse than this for alerts (Alerts:MaxAccuracyMeters). */
export const MAX_ACCURACY_METERS = 500;
/** Geofence around the last report. Below ~200 m geofences are unreliable on both platforms. */
export const GEOFENCE_RADIUS_METERS = 500;
/** Check-in interval, in minutes, for expo-background-task (OS minimum 15). */
export const CHECKIN_INTERVAL_MINUTES = 60;
/** The API rejects fixes older than 12 hours; stay well inside that. */
const MAX_REPORT_AGE_MS = 6 * 60 * 60_000;

/** The last fix the API accepted from this phone. */
export interface LastReport {
  latitude: number;
  longitude: number;
  capturedAt: string;
  sentAt: number;
}

export function distanceMeters(a: { latitude: number; longitude: number }, b: { latitude: number; longitude: number }): number {
  const R = 6_371_008.8;
  const toRad = (d: number) => (d * Math.PI) / 180;
  const dLat = toRad(b.latitude - a.latitude);
  const dLng = toRad(b.longitude - a.longitude);
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(toRad(a.latitude)) * Math.cos(toRad(b.latitude)) * Math.sin(dLng / 2) ** 2;
  return 2 * R * Math.asin(Math.min(1, Math.sqrt(h)));
}

/**
 * The fix worth reporting from an OS batch: the newest one accurate enough for alerts.
 * Batches can arrive out of order, and can contain stale or inaccurate points.
 */
export function pickReportableFix(positions: readonly RawPosition[], now: number): Fix | null {
  let best: Fix | null = null;
  for (const position of positions) {
    const fix = toFix(position);
    if (!fix) continue;
    if ((fix.accuracyMeters ?? 0) > MAX_ACCURACY_METERS) continue;
    const at = Date.parse(fix.capturedAt);
    if (now - at > MAX_REPORT_AGE_MS) continue;
    if (!best || at > Date.parse(best.capturedAt)) best = fix;
  }
  return best;
}

/** Whether a new fix is worth a network request, given what the API already has from this phone. */
export function shouldReport(last: LastReport | null, fix: Fix, now: number): boolean {
  if (!last) return true;
  if (Date.parse(fix.capturedAt) <= Date.parse(last.capturedAt)) return false; // not newer: the API would ignore it
  if (now < last.sentAt) return true; // clock moved back: do not trust the throttle
  if (now - last.sentAt >= KEEPALIVE_MS) return true;
  return distanceMeters(last, fix) >= MIN_MOVE_METERS;
}

/**
 * What to do after a report attempt:
 * - `sent`: remember it as the last report;
 * - `keyInvalid`: the key was revoked or the phone's session ended; stop background work until the app is next opened;
 * - `consentWithdrawn`: location sharing was turned off elsewhere; stop and forget;
 * - `skip`: this fix was refused (too old, clock skew), the next one may be fine;
 * - `retry`: offline, rate-limited or a server error; try with the next fix.
 */
export type ReportOutcome = 'sent' | 'keyInvalid' | 'consentWithdrawn' | 'skip' | 'retry';

export function outcomeOf(error: unknown): Exclude<ReportOutcome, 'sent'> {
  if (error instanceof NetworkError) return 'retry';
  if (error instanceof ApiError) {
    if (error.status === 401) return 'keyInvalid';
    if (error.status === 422 && error.code === 'consent.location_required') return 'consentWithdrawn';
    if (error.status === 400 || error.status === 422) return 'skip';
    return 'retry';
  }
  return 'retry';
}

/** The circle whose exit wakes the app (notifies on exit only). */
export function geofenceAround(point: { latitude: number; longitude: number }) {
  return {
    identifier: 'community-sos-last-report',
    latitude: point.latitude,
    longitude: point.longitude,
    radius: GEOFENCE_RADIUS_METERS,
    notifyOnEnter: false,
    notifyOnExit: true,
  };
}
