import { ApiError, NetworkError } from '@/api/errors';

import {
  INDIA_REGION,
  classifyReviewError,
  expiryFromPreset,
  minutesSince,
  parseMaxUses,
  periodStart,
  regionAround,
  regionToBounds,
  splitDuration,
} from '../logic';

const NOW = Date.parse('2026-11-15T10:30:45.678Z');

describe('time helpers', () => {
  it('counts whole minutes waited and never goes negative on clock skew', () => {
    expect(minutesSince('2026-11-15T10:26:00Z', NOW)).toBe(4);
    expect(minutesSince('2026-11-15T10:31:30Z', NOW)).toBe(0);
    expect(minutesSince('not a date', NOW)).toBe(0);
  });

  it('splits the average review time into minutes and seconds', () => {
    expect(splitDuration(74.5)).toEqual({ minutes: 1, seconds: 15 });
    expect(splitDuration(59.4)).toEqual({ minutes: 0, seconds: 59 });
    expect(splitDuration(0)).toEqual({ minutes: 0, seconds: 0 });
  });

  it('computes period starts rounded to the minute (stable query keys)', () => {
    expect(periodStart('all', NOW)).toBeUndefined();
    expect(periodStart('24h', NOW)).toBe('2026-11-14T10:30:00.000Z');
    expect(periodStart('7d', NOW)).toBe('2026-11-08T10:30:00.000Z');
    expect(periodStart('7d', NOW + 10_000)).toBe(periodStart('7d', NOW));
  });
});

describe('invitation form', () => {
  it('turns expiry presets into timestamps (never = no expiry)', () => {
    expect(expiryFromPreset('never', NOW)).toBeUndefined();
    expect(expiryFromPreset('1d', NOW)).toBe('2026-11-16T10:30:45.678Z');
    expect(expiryFromPreset('90d', NOW)).toBe('2027-02-13T10:30:45.678Z');
  });

  it('accepts a blank or 1–10,000 sign-up limit', () => {
    expect(parseMaxUses('')).toEqual({ ok: true, value: undefined });
    expect(parseMaxUses('  ')).toEqual({ ok: true, value: undefined });
    expect(parseMaxUses('50')).toEqual({ ok: true, value: 50 });
    expect(parseMaxUses('10000')).toEqual({ ok: true, value: 10_000 });
    expect(parseMaxUses('0')).toEqual({ ok: false });
    expect(parseMaxUses('10001')).toEqual({ ok: false });
    expect(parseMaxUses('1.5')).toEqual({ ok: false });
    expect(parseMaxUses('-3')).toEqual({ ok: false });
  });
});

describe('regionToBounds', () => {
  it('covers the visible region, snapped outwards', () => {
    const region = regionAround(23.0225, 72.5714);
    const b = regionToBounds(region);
    expect(b.minLatitude).toBeLessThanOrEqual(region.latitude - region.latitudeDelta / 2);
    expect(b.maxLatitude).toBeGreaterThanOrEqual(region.latitude + region.latitudeDelta / 2);
    expect(b.minLongitude).toBeLessThanOrEqual(region.longitude - region.longitudeDelta / 2);
    expect(b.maxLongitude).toBeGreaterThanOrEqual(region.longitude + region.longitudeDelta / 2);
  });

  it('gives the same box for a small pan, so the cached pins are reused', () => {
    const a = regionToBounds(regionAround(23.0225, 72.5714));
    const b = regionToBounds(regionAround(23.0231, 72.572));
    expect(b).toEqual(a);
  });

  it('stays within valid coordinates with max > min (API validation)', () => {
    const world = regionToBounds({ latitude: 0, longitude: 0, latitudeDelta: 400, longitudeDelta: 400 });
    expect(world).toEqual({ minLatitude: -90, maxLatitude: 90, minLongitude: -180, maxLongitude: 180 });

    const pole = regionToBounds({ latitude: 90, longitude: 180, latitudeDelta: 0, longitudeDelta: 0 });
    expect(pole.maxLatitude).toBeGreaterThan(pole.minLatitude);
    expect(pole.maxLongitude).toBeGreaterThan(pole.minLongitude);
    expect(pole.maxLatitude).toBeLessThanOrEqual(90);
    expect(pole.maxLongitude).toBeLessThanOrEqual(180);
  });

  it('handles the all-India start view', () => {
    const b = regionToBounds(INDIA_REGION);
    expect(b.minLatitude).toBeLessThanOrEqual(9.5);
    expect(b.maxLatitude).toBeGreaterThanOrEqual(35.5);
  });
});

describe('classifyReviewError', () => {
  it('maps the documented approve/reject failures', () => {
    expect(classifyReviewError(new ApiError(409, { code: 'concurrency_conflict' }))).toBe('conflict');
    expect(classifyReviewError(new ApiError(422, { code: 'sos.already_reviewed' }))).toBe('alreadyReviewed');
    expect(classifyReviewError(new ApiError(404, { code: 'sos.not_found' }))).toBe('notFound');
    expect(classifyReviewError(new ApiError(400, { code: 'validation_failed' }))).toBe('other');
    expect(classifyReviewError(new NetworkError('offline', false))).toBe('other');
  });
});
