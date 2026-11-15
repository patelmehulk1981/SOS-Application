jest.mock('@/api/endpoints', () => ({ meApi: { updateLocation: jest.fn() } }));
jest.mock('@/auth/session', () => ({ registerSignOutCleanup: jest.fn() }));
jest.mock('../useCurrentLocation', () => ({ getOneFix: jest.fn() }));

/* eslint-disable import/first */
import { meApi } from '@/api/endpoints';
import { HEARTBEAT_INTERVAL_MS, resetHeartbeat, sendHeartbeat, shouldSendHeartbeat } from '../heartbeat';
import { getOneFix } from '../useCurrentLocation';
/* eslint-enable import/first */

const updateLocation = meApi.updateLocation as jest.Mock;
const oneFix = getOneFix as jest.Mock;
const FIX = { latitude: 23.0225, longitude: 72.5714, accuracyMeters: 12, capturedAt: '2026-11-15T10:00:00.000Z' };
const T0 = Date.parse('2026-11-15T10:00:00Z');

beforeEach(() => {
  jest.clearAllMocks();
  resetHeartbeat();
});

describe('shouldSendHeartbeat', () => {
  it('sends the first time, then at most every 15 minutes', () => {
    expect(shouldSendHeartbeat(null, T0)).toBe(true);
    expect(shouldSendHeartbeat(T0, T0 + HEARTBEAT_INTERVAL_MS - 1)).toBe(false);
    expect(shouldSendHeartbeat(T0, T0 + HEARTBEAT_INTERVAL_MS)).toBe(true);
  });

  it('sends again if the clock went backwards', () => {
    expect(shouldSendHeartbeat(T0, T0 - 60_000)).toBe(true);
  });
});

describe('sendHeartbeat', () => {
  it('sends a fix, then waits out the interval', async () => {
    oneFix.mockResolvedValue(FIX);
    updateLocation.mockResolvedValue(undefined);

    await expect(sendHeartbeat(T0)).resolves.toBe(true);
    await expect(sendHeartbeat(T0 + 60_000)).resolves.toBe(false);
    await expect(sendHeartbeat(T0 + HEARTBEAT_INTERVAL_MS)).resolves.toBe(true);

    expect(updateLocation).toHaveBeenCalledTimes(2);
    expect(updateLocation).toHaveBeenCalledWith(FIX);
  });

  it('does nothing without permission or a fix (never prompts)', async () => {
    oneFix.mockResolvedValue(null);
    await expect(sendHeartbeat(T0)).resolves.toBe(false);
    expect(updateLocation).not.toHaveBeenCalled();
  });

  it('retries on the next foreground after a failure', async () => {
    oneFix.mockResolvedValue(FIX);
    updateLocation.mockRejectedValueOnce(new Error('offline')).mockResolvedValueOnce(undefined);

    await expect(sendHeartbeat(T0)).resolves.toBe(false);
    await expect(sendHeartbeat(T0 + 1_000)).resolves.toBe(true);
  });
});
