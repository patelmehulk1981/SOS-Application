import * as Application from 'expo-application';
import Constants from 'expo-constants';
import * as Crypto from 'expo-crypto';
import * as Device from 'expo-device';
import { Platform } from 'react-native';

import type { DevicePlatform } from '@/api/types';
import { StorageKeys, secureStorage } from './secureStorage';

export const appVersion: string = Application.nativeApplicationVersion ?? Constants.expoConfig?.version ?? '0.0.0';

export const platform: DevicePlatform = Platform.OS === 'ios' ? 'Ios' : 'Android';

export const deviceModel: string | undefined = Device.modelName ?? undefined;

/** Recorded by the API as the source of each consent (docs/03b-auth-profile.md). */
export const userAgent = `CommunitySos/${appVersion} (${Platform.OS === 'ios' ? 'ios' : 'android'})`;

let cachedDeviceId: Promise<string> | null = null;

/**
 * Random per-install id sent with login/refresh so sessions can be listed and revoked per device.
 * Not a hardware identifier: it is regenerated after a reinstall.
 */
export function getDeviceId(): Promise<string> {
  cachedDeviceId ??= (async () => {
    const existing = await secureStorage.get(StorageKeys.deviceId);
    if (existing) return existing;
    const created = Crypto.randomUUID();
    await secureStorage.set(StorageKeys.deviceId, created);
    return created;
  })().catch((error: unknown) => {
    cachedDeviceId = null;
    throw error;
  });
  return cachedDeviceId;
}
