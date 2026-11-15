import { ApiError, NetworkError } from '@/api/errors';

import {
  KEEPALIVE_MS,
  MIN_MOVE_METERS,
  distanceMeters,
  geofenceAround,
  outcomeOf,
  pickReportableFix,
  shouldReport,
  type LastReport,
} from '../policy';

const T0 = Date.parse('2026-11-15T10:00:00Z');
const HOME = { latitude: 23.0225, longitude: 72.5714 };

function position(at: number, accuracy: number | null = 30, latitude = HOME.latitude, longitude = HOME.longitude) {
  return { coords: { latitude, longitude, accuracy }, timestamp: at };
}

function fixAt(at: number, latitude = HOME.latitude) {
  return { latitude, longitude: HOME.longitude, accuracyMeters: 30, capturedAt: new Date(at).toISOString() };
}

const last: LastReport = { ...HOME, capturedAt: new Date(T0).toISOString(), sentAt: T0 };

describe('distanceMeters', () => {
  it('matches a known distance (0.01° latitude ≈ 1.11 km)', () => {
    expect(distanceMeters(HOME, { ...HOME, latitude: HOME.latitude + 0.01 })).toBeCloseTo(1112, -1);
    expect(distanceMeters(HOME, HOME)).toBe(0);
  });
});

describe('pickReportableFix', () => {
  it('takes the newest accurate fix of an out-of-order batch', () => {
    const fix = pickReportableFix([position(T0 - 60_000, 20, 1.5, 1.5), position(T0, 40, 2.5, 2.5), position(T0 - 120_000, 10)], T0);
    expect(fix?.latitude).toBe(2.5);
  });

  it('skips inaccurate, stale and null-island fixes', () => {
    expect(pickReportableFix([position(T0, 900)], T0)).toBeNull();
    expect(pickReportableFix([position(T0 - 7 * 60 * 60_000)], T0)).toBeNull();
    expect(pickReportableFix([position(T0, 10, 0, 0)], T0)).toBeNull();
    expect(pickReportableFix([], T0)).toBeNull();
  });

  it('accepts a fix without an accuracy value', () => {
    expect(pickReportableFix([position(T0, null)], T0)).not.toBeNull();
  });
});

describe('shouldReport', () => {
  it('always reports the first fix', () => {
    expect(shouldReport(null, fixAt(T0), T0)).toBe(true);
  });

  it('waits until the phone has moved or the keep-alive is due', () => {
    const nearby = fixAt(T0 + 60_000, HOME.latitude + 0.001); // ~110 m
    expect(shouldReport(last, nearby, T0 + 60_000)).toBe(false);

    const moved = fixAt(T0 + 60_000, HOME.latitude + (MIN_MOVE_METERS + 20) / 111_195);
    expect(shouldReport(last, moved, T0 + 60_000)).toBe(true);

    expect(shouldReport(last, fixAt(T0 + KEEPALIVE_MS), T0 + KEEPALIVE_MS)).toBe(true);
    expect(shouldReport(last, fixAt(T0 + KEEPALIVE_MS - 1), T0 + KEEPALIVE_MS - 1)).toBe(false);
  });

  it('never sends a fix that is not newer than the last report', () => {
    expect(shouldReport(last, fixAt(T0, HOME.latitude + 1), T0 + KEEPALIVE_MS)).toBe(false);
  });

  it('reports if the clock went backwards', () => {
    expect(shouldReport(last, fixAt(T0 + 1_000), T0 - 60_000)).toBe(true);
  });
});

describe('outcomeOf', () => {
  const problem = (status: number, code?: string) => new ApiError(status, { status, code });

  it('stops on a dead key and on withdrawn consent', () => {
    expect(outcomeOf(problem(401, 'location_key.invalid'))).toBe('keyInvalid');
    expect(outcomeOf(problem(422, 'consent.location_required'))).toBe('consentWithdrawn');
  });

  it('skips a refused fix and retries transient failures', () => {
    expect(outcomeOf(problem(422, 'location.stale'))).toBe('skip');
    expect(outcomeOf(problem(400, 'validation_failed'))).toBe('skip');
    expect(outcomeOf(problem(429))).toBe('retry');
    expect(outcomeOf(problem(503))).toBe('retry');
    expect(outcomeOf(new NetworkError('offline', false))).toBe('retry');
  });
});

describe('geofenceAround', () => {
  it('wakes on exit only, with a reliable radius', () => {
    const region = geofenceAround(HOME);
    expect(region).toMatchObject({ ...HOME, notifyOnEnter: false, notifyOnExit: true });
    expect(region.radius).toBeGreaterThanOrEqual(200);
  });
});
