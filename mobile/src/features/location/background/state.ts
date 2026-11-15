import AsyncStorage from '@react-native-async-storage/async-storage';

import { StorageKeys, secureStorage } from '@/lib/secureStorage';

import type { LastReport } from './policy';

/**
 * Per-phone background location state. The key is a secret (secure storage); the rest is not and
 * lives in AsyncStorage, which background tasks can read too.
 */
export interface BackgroundState {
  /** The person turned background location on in this app (and has not turned it off). */
  enabled: boolean;
  lastReport: LastReport | null;
  /** Background work stopped itself because the key stopped working; the app re-arms it when opened. */
  needsNewKey: boolean;
}

const STATE_KEY = 'location.background.v1';
const EMPTY: BackgroundState = { enabled: false, lastReport: null, needsNewKey: false };

export async function readState(): Promise<BackgroundState> {
  try {
    const raw = await AsyncStorage.getItem(STATE_KEY);
    if (!raw) return EMPTY;
    const parsed = JSON.parse(raw) as Partial<BackgroundState>;
    return {
      enabled: parsed.enabled === true,
      lastReport: isLastReport(parsed.lastReport) ? parsed.lastReport : null,
      needsNewKey: parsed.needsNewKey === true,
    };
  } catch {
    return EMPTY;
  }
}

export async function writeState(change: Partial<BackgroundState>): Promise<BackgroundState> {
  const next = { ...(await readState()), ...change };
  await AsyncStorage.setItem(STATE_KEY, JSON.stringify(next));
  return next;
}

export async function clearState(): Promise<void> {
  await AsyncStorage.removeItem(STATE_KEY);
}

export const locationKeyStore = {
  get: () => secureStorage.get(StorageKeys.locationKey),
  set: (key: string) => secureStorage.set(StorageKeys.locationKey, key),
  clear: () => secureStorage.remove(StorageKeys.locationKey),
};

function isLastReport(value: unknown): value is LastReport {
  if (!value || typeof value !== 'object') return false;
  const v = value as Record<string, unknown>;
  return typeof v.latitude === 'number' && typeof v.longitude === 'number' && typeof v.capturedAt === 'string' && typeof v.sentAt === 'number';
}
