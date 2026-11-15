/** A GPS fix as the API expects it (CreateSosRequest / UpdateLocationRequest). */
export interface Fix {
  latitude: number;
  longitude: number;
  accuracyMeters?: number;
  /** ISO-8601 UTC time the device took the fix. */
  capturedAt: string;
}

/** The API rejects SOS fixes older than 30 minutes; stay well inside that. */
export const MAX_FIX_AGE_MS = 20 * 60_000;
/** Good enough to stop showing "improving accuracy". */
export const GOOD_ACCURACY_METERS = 50;
/** A newer fix replaces an older, more accurate one once the older one is this stale (the person may be moving). */
const PREFER_NEWER_AFTER_MS = 60_000;

export interface RawPosition {
  coords: { latitude: number; longitude: number; accuracy: number | null };
  timestamp: number;
}

/** Converts an expo-location position; null for unusable fixes (NaN, out of range, or the 0,0 "null island"). */
export function toFix(position: RawPosition): Fix | null {
  const { latitude, longitude, accuracy } = position.coords;
  if (!Number.isFinite(latitude) || !Number.isFinite(longitude)) return null;
  if (Math.abs(latitude) > 90 || Math.abs(longitude) > 180) return null;
  if (latitude === 0 && longitude === 0) return null;
  return {
    latitude,
    longitude,
    accuracyMeters: accuracy !== null && Number.isFinite(accuracy) && accuracy >= 0 ? Math.round(accuracy * 10) / 10 : undefined,
    capturedAt: new Date(position.timestamp).toISOString(),
  };
}

/** Keeps the more useful of two fixes: more accurate, unless the current one has gone stale. */
export function pickBetterFix(current: Fix | null, candidate: Fix | null): Fix | null {
  if (!candidate) return current;
  if (!current) return candidate;
  const ageGap = Date.parse(candidate.capturedAt) - Date.parse(current.capturedAt);
  if (ageGap >= PREFER_NEWER_AFTER_MS) return candidate;
  if (ageGap < 0) return current; // out-of-order delivery
  const currentAcc = current.accuracyMeters ?? Number.POSITIVE_INFINITY;
  const candidateAcc = candidate.accuracyMeters ?? Number.POSITIVE_INFINITY;
  return candidateAcc <= currentAcc ? candidate : current;
}

export function isFixFresh(fix: Fix, now: number = Date.now()): boolean {
  return now - Date.parse(fix.capturedAt) <= MAX_FIX_AGE_MS;
}
