jest.mock('@react-native-async-storage/async-storage', () =>
  jest.requireActual('@react-native-async-storage/async-storage/jest/async-storage-mock'),
);
jest.mock('@/api/endpoints', () => ({ backgroundLocationApi: { report: jest.fn() } }));
jest.mock('@/lib/secureStorage', () => {
  const values = new Map<string, string>();
  return {
    StorageKeys: { locationKey: 'location.key' },
    secureStorage: {
      get: jest.fn(async (k: string) => values.get(k) ?? null),
      set: jest.fn(async (k: string, v: string) => void values.set(k, v)),
      remove: jest.fn(async (k: string) => void values.delete(k)),
    },
  };
});
jest.mock('../native', () => ({ stopAll: jest.fn(async () => undefined), recenterGeofence: jest.fn(async () => undefined) }));

/* eslint-disable import/first */
import AsyncStorage from '@react-native-async-storage/async-storage';

import { backgroundLocationApi } from '@/api/endpoints';
import { ApiError, NetworkError } from '@/api/errors';

import { recenterGeofence, stopAll } from '../native';
import { KEEPALIVE_MS } from '../policy';
import { reportFix } from '../reporter';
import { locationKeyStore, readState, writeState } from '../state';
/* eslint-enable import/first */

const report = backgroundLocationApi.report as jest.Mock;
const T0 = Date.parse('2026-11-15T10:00:00Z');
const fixAt = (at: number, latitude = 23.0225) => ({ latitude, longitude: 72.5714, accuracyMeters: 30, capturedAt: new Date(at).toISOString() });

beforeEach(async () => {
  jest.clearAllMocks();
  await AsyncStorage.clear();
  await locationKeyStore.clear();
  await locationKeyStore.set('lk_test');
  await writeState({ enabled: true });
});

it('sends with the location key, remembers the report and moves the geofence', async () => {
  report.mockResolvedValue(undefined);

  await expect(reportFix(fixAt(T0), T0)).resolves.toBe('sent');

  expect(report).toHaveBeenCalledWith('lk_test', fixAt(T0));
  expect((await readState()).lastReport).toMatchObject({ latitude: 23.0225, sentAt: T0 });
  expect(recenterGeofence).toHaveBeenCalledWith(fixAt(T0));
});

it('throttles until the phone moves or the keep-alive is due', async () => {
  report.mockResolvedValue(undefined);
  await reportFix(fixAt(T0), T0);

  await expect(reportFix(fixAt(T0 + 60_000), T0 + 60_000)).resolves.toBe('throttled');
  await expect(reportFix(fixAt(T0 + KEEPALIVE_MS), T0 + KEEPALIVE_MS)).resolves.toBe('sent');
  expect(report).toHaveBeenCalledTimes(2);
});

it('does nothing when turned off or without a key', async () => {
  await writeState({ enabled: false });
  await expect(reportFix(fixAt(T0), T0)).resolves.toBe('disabled');

  await writeState({ enabled: true });
  await locationKeyStore.clear();
  await expect(reportFix(fixAt(T0), T0)).resolves.toBe('noKey');
  expect(report).not.toHaveBeenCalled();
});

it('a dead key stops background work and waits for the app to re-arm it', async () => {
  report.mockRejectedValue(new ApiError(401, { status: 401, code: 'location_key.invalid' }));

  await expect(reportFix(fixAt(T0), T0)).resolves.toBe('keyInvalid');

  expect(stopAll).toHaveBeenCalled();
  expect(await locationKeyStore.get()).toBeNull();
  expect(await readState()).toMatchObject({ enabled: true, needsNewKey: true });
  await expect(reportFix(fixAt(T0 + KEEPALIVE_MS), T0 + KEEPALIVE_MS)).resolves.toBe('disabled');
});

it('withdrawn consent turns background location off', async () => {
  report.mockRejectedValue(new ApiError(422, { status: 422, code: 'consent.location_required' }));

  await expect(reportFix(fixAt(T0), T0)).resolves.toBe('consentWithdrawn');

  expect(await readState()).toMatchObject({ enabled: false, lastReport: null });
});

it('keeps going after a network failure, and does not record the failed fix', async () => {
  report.mockRejectedValueOnce(new NetworkError('offline', false)).mockResolvedValueOnce(undefined);

  await expect(reportFix(fixAt(T0), T0)).resolves.toBe('retry');
  expect((await readState()).lastReport).toBeNull();
  expect(stopAll).not.toHaveBeenCalled();

  await expect(reportFix(fixAt(T0 + 1_000), T0 + 1_000)).resolves.toBe('sent');
});

it('concurrent tasks share one request', async () => {
  report.mockImplementation(() => new Promise<void>((resolve) => setTimeout(resolve, 10)));

  const a = reportFix(fixAt(T0), T0);
  const b = reportFix(fixAt(T0), T0);

  await expect(Promise.all([a, b])).resolves.toEqual(['sent', 'sent']);
  expect(report).toHaveBeenCalledTimes(1);
});
