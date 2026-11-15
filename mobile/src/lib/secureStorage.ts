import * as SecureStore from 'expo-secure-store';

/**
 * Small key-value store backed by the iOS Keychain / Android Keystore.
 * AFTER_FIRST_UNLOCK_THIS_DEVICE_ONLY: readable by background work after the first unlock
 * (needed for location updates in Module 6), and never copied into backups or onto a new phone.
 */
const options: SecureStore.SecureStoreOptions = {
  keychainAccessible: SecureStore.AFTER_FIRST_UNLOCK_THIS_DEVICE_ONLY,
};

export const StorageKeys = {
  refreshToken: 'auth.refreshToken',
  sessionUser: 'auth.sessionUser',
  deviceId: 'device.installId',
  /** Background location key (docs/06); readable by background tasks after first unlock. */
  locationKey: 'location.key',
} as const;

export type StorageKey = (typeof StorageKeys)[keyof typeof StorageKeys];

export interface KeyValueStore {
  get(key: StorageKey): Promise<string | null>;
  set(key: StorageKey, value: string): Promise<void>;
  remove(key: StorageKey): Promise<void>;
}

export const secureStorage: KeyValueStore = {
  get: (key) => SecureStore.getItemAsync(key, options),
  set: (key, value) => SecureStore.setItemAsync(key, value, options),
  remove: (key) => SecureStore.deleteItemAsync(key, options),
};
