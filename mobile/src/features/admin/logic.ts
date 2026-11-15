import { ApiError } from '@/api/errors';
import { MAX_INVITATION_USES, type MapBounds } from '@/api/types';

/** Pure helpers for the admin screens (unit-tested in __tests__/logic.test.ts). */

// ---- Time ---------------------------------------------------------------------------------------

/** Whole minutes between `iso` and `now` (never negative: device clocks drift). */
export function minutesSince(iso: string, now: number = Date.now()): number {
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) return 0;
  return Math.max(0, Math.floor((now - then) / 60_000));
}

/** 74.5 → { minutes: 1, seconds: 15 } (rounded to whole seconds). */
export function splitDuration(totalSeconds: number): { minutes: number; seconds: number } {
  const rounded = Math.max(0, Math.round(totalSeconds));
  return { minutes: Math.floor(rounded / 60), seconds: rounded % 60 };
}

export type Period = '24h' | '7d' | '30d' | 'all';
export const PERIODS: readonly Period[] = ['24h', '7d', '30d', 'all'];

const PERIOD_MS: Record<Exclude<Period, 'all'>, number> = {
  '24h': 24 * 3_600_000,
  '7d': 7 * 86_400_000,
  '30d': 30 * 86_400_000,
};

/**
 * `from` for a period filter, rounded down to the minute so the query key (and cache) stays stable
 * across re-renders within that minute.
 */
export function periodStart(period: Period, now: number = Date.now()): string | undefined {
  if (period === 'all') return undefined;
  const minute = Math.floor(now / 60_000) * 60_000;
  return new Date(minute - PERIOD_MS[period]).toISOString();
}

// ---- Invitations --------------------------------------------------------------------------------

export type ExpiryPreset = 'never' | '1d' | '7d' | '30d' | '90d';
export const EXPIRY_PRESETS: readonly ExpiryPreset[] = ['never', '1d', '7d', '30d', '90d'];

const EXPIRY_DAYS: Record<Exclude<ExpiryPreset, 'never'>, number> = { '1d': 1, '7d': 7, '30d': 30, '90d': 90 };

export function expiryFromPreset(preset: ExpiryPreset, now: number = Date.now()): string | undefined {
  if (preset === 'never') return undefined;
  return new Date(now + EXPIRY_DAYS[preset] * 86_400_000).toISOString();
}

export type MaxUsesResult = { ok: true; value: number | undefined } | { ok: false };

/** Blank = unlimited; otherwise a whole number 1–10,000 (the API limit). */
export function parseMaxUses(text: string): MaxUsesResult {
  const trimmed = text.trim();
  if (trimmed === '') return { ok: true, value: undefined };
  if (!/^\d+$/.test(trimmed)) return { ok: false };
  const value = Number(trimmed);
  return value >= 1 && value <= MAX_INVITATION_USES ? { ok: true, value } : { ok: false };
}

// ---- Map ----------------------------------------------------------------------------------------

export interface Region {
  latitude: number;
  longitude: number;
  latitudeDelta: number;
  longitudeDelta: number;
}

const clamp = (value: number, min: number, max: number) => Math.min(max, Math.max(min, value));

/**
 * The query box for a visible map region. Edges are snapped outwards to a grid of ~1/8 of the
 * span, so small pans and zoom jitter reuse the cached result instead of refetching. Clamped to valid
 * coordinates; the API does not support boxes across the 180° meridian (irrelevant for India).
 */
export function regionToBounds(region: Region): MapBounds {
  const latSpan = Math.max(region.latitudeDelta, 0.001);
  const lonSpan = Math.max(region.longitudeDelta, 0.001);
  const step = gridStep(Math.max(latSpan, lonSpan) / 8);
  const snapDown = (v: number) => Math.floor(v / step) * step;
  const snapUp = (v: number) => Math.ceil(v / step) * step;
  const round = (v: number) => Math.round(v * 1e6) / 1e6;

  const bounds = {
    minLatitude: round(clamp(snapDown(region.latitude - latSpan / 2), -90, 90)),
    maxLatitude: round(clamp(snapUp(region.latitude + latSpan / 2), -90, 90)),
    minLongitude: round(clamp(snapDown(region.longitude - lonSpan / 2), -180, 180)),
    maxLongitude: round(clamp(snapUp(region.longitude + lonSpan / 2), -180, 180)),
  };
  // The API requires max > min.
  if (bounds.maxLatitude <= bounds.minLatitude) bounds.maxLatitude = Math.min(90, bounds.minLatitude + step);
  if (bounds.maxLongitude <= bounds.minLongitude) bounds.maxLongitude = Math.min(180, bounds.minLongitude + step);
  return bounds;
}

/** Rounds a raw step up to 1, 2 or 5 × 10^n, so neighbouring zoom levels share grid lines. */
function gridStep(raw: number): number {
  const magnitude = 10 ** Math.floor(Math.log10(raw));
  const normalised = raw / magnitude;
  const nice = normalised <= 1 ? 1 : normalised <= 2 ? 2 : normalised <= 5 ? 5 : 10;
  return nice * magnitude;
}

/** Whole of India: the starting view when the admin's own location is unknown. */
export const INDIA_REGION: Region = { latitude: 22.5, longitude: 79.0, latitudeDelta: 26, longitudeDelta: 26 };

/** ~10 km across, centred on a point: the starting view around the admin. */
export function regionAround(latitude: number, longitude: number): Region {
  return { latitude, longitude, latitudeDelta: 0.09, longitudeDelta: 0.09 };
}

// ---- Review errors ------------------------------------------------------------------------------

export type ReviewFailure = 'conflict' | 'alreadyReviewed' | 'notFound' | 'other';

/** How an approve/reject failure should be handled (docs/03e error table). */
export function classifyReviewError(error: unknown): ReviewFailure {
  if (!(error instanceof ApiError)) return 'other';
  if (error.code === 'concurrency_conflict' || error.status === 409) return 'conflict';
  if (error.code === 'sos.already_reviewed') return 'alreadyReviewed';
  if (error.code === 'sos.not_found' || error.status === 404) return 'notFound';
  return 'other';
}
