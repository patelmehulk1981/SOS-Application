import { MAX_FIX_AGE_MS, isFixFresh, pickBetterFix, toFix, type Fix } from '../fix';

const T0 = Date.parse('2026-11-15T10:00:00Z');
const fix = (accuracyMeters: number | undefined, offsetMs = 0): Fix => ({
  latitude: 23.0225,
  longitude: 72.5714,
  accuracyMeters,
  capturedAt: new Date(T0 + offsetMs).toISOString(),
});

describe('toFix', () => {
  it('converts a position', () => {
    expect(toFix({ coords: { latitude: 23.02251, longitude: 72.57136, accuracy: 8.46 }, timestamp: T0 })).toEqual({
      latitude: 23.02251,
      longitude: 72.57136,
      accuracyMeters: 8.5,
      capturedAt: '2026-11-15T10:00:00.000Z',
    });
  });

  it.each([
    [0, 0],
    [Number.NaN, 72],
    [91, 72],
    [23, 181],
  ])('rejects unusable coordinates (%p, %p)', (latitude, longitude) => {
    expect(toFix({ coords: { latitude, longitude, accuracy: 5 }, timestamp: T0 })).toBeNull();
  });

  it('omits an unknown accuracy', () => {
    expect(toFix({ coords: { latitude: 23, longitude: 72, accuracy: null }, timestamp: T0 })?.accuracyMeters).toBeUndefined();
  });
});

describe('pickBetterFix', () => {
  it('prefers the more accurate fix of similar age', () => {
    expect(pickBetterFix(fix(80), fix(12, 2_000))).toEqual(fix(12, 2_000));
    expect(pickBetterFix(fix(12), fix(80, 2_000))).toEqual(fix(12));
  });

  it('prefers a much newer fix even if less accurate (the person may be moving)', () => {
    expect(pickBetterFix(fix(10), fix(40, 61_000))).toEqual(fix(40, 61_000));
  });

  it('ignores out-of-order fixes and missing candidates', () => {
    expect(pickBetterFix(fix(50, 5_000), fix(5, 0))).toEqual(fix(50, 5_000));
    expect(pickBetterFix(fix(50), null)).toEqual(fix(50));
    expect(pickBetterFix(null, fix(50))).toEqual(fix(50));
  });
});

describe('isFixFresh', () => {
  it('expires fixes older than the API allows (with margin)', () => {
    expect(isFixFresh(fix(10), T0 + MAX_FIX_AGE_MS)).toBe(true);
    expect(isFixFresh(fix(10), T0 + MAX_FIX_AGE_MS + 1)).toBe(false);
  });
});
