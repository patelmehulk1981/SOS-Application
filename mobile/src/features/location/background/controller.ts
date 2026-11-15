import { useEffect } from 'react';
import { AppState } from 'react-native';
import { create } from 'zustand';

import { meApi } from '@/api/endpoints';
import { isApiError } from '@/api/errors';
import { registerSignOutCleanup } from '@/auth/session';
import { getDeviceId } from '@/lib/device';

import { getPermission, isAvailable, requestPermission, startAll, stopAll, type PermissionState } from './native';
import { clearState, locationKeyStore, readState, writeState } from './state';

/**
 * Turning background location on/off, and keeping it consistent each time the app opens
 * (docs/06-background-location.md). The UI reads `useBackgroundLocationStore`.
 */

export type BackgroundStatus =
  | 'unavailable' // this build/device cannot run background location (Expo Go, web)
  | 'off' // not turned on in the app
  | 'on' // running
  | 'needsPermission' // turned on, but "Allow all the time" / "Always" was taken away in Settings
  | 'needsSharing'; // tried to turn on while location sharing (consent) is off

interface BackgroundLocationStore {
  status: BackgroundStatus;
  permission: PermissionState | null;
}

export const useBackgroundLocationStore = create<BackgroundLocationStore>(() => ({ status: 'off', permission: null }));

export type EnableResult = 'on' | 'needsPermission' | 'needsSharing' | 'unavailable' | 'failed';

/** After the disclosure screen: asks for permission, obtains a key and starts the tasks. */
export async function enableBackgroundLocation(locationSharing: boolean): Promise<EnableResult> {
  if (!(await isAvailable())) return publish('unavailable');
  if (!locationSharing) return publish('needsSharing');

  const permission = await requestPermission();
  useBackgroundLocationStore.setState({ permission });
  if (permission.status !== 'granted') return 'needsPermission';

  try {
    await issueKey();
    await writeState({ enabled: true, needsNewKey: false });
    await startAll();
  } catch (error) {
    await stopAll();
    if (isApiError(error, 'consent.location_required')) return publish('needsSharing');
    publish('off');
    return 'failed';
  }
  return publish('on');
}

/** Turning it off: stops everything on the phone, then revokes the key on the server (best effort). */
export async function disableBackgroundLocation(): Promise<void> {
  await stopAll();
  await locationKeyStore.clear().catch(() => undefined);
  await writeState({ enabled: false, needsNewKey: false, lastReport: null });
  publish('off');
  try {
    await meApi.revokeLocationKey(await getDeviceId());
  } catch {
    // Offline: the key is already gone from the phone, and expires with this phone's session anyway.
  }
}

/**
 * Reconciles on app start and every return to the foreground: restarts tasks the OS dropped
 * (reboot on iOS, force-stop on Android), replaces a key that stopped working, and stops everything
 * when consent or permission is gone.
 */
export async function syncBackgroundLocation(locationSharing: boolean): Promise<BackgroundStatus> {
  if (!(await isAvailable())) return publish('unavailable');
  const state = await readState();
  const permission = await getPermission();
  useBackgroundLocationStore.setState({ permission });

  if (!state.enabled) {
    await stopAll(); // leftovers from an interrupted turn-off
    return publish('off');
  }
  if (!locationSharing) {
    // Withdrawing location sharing also withdraws background collection: it is not resumed silently
    // if sharing is turned back on later (the server has already revoked the key).
    await stopAll();
    await locationKeyStore.clear().catch(() => undefined);
    await writeState({ enabled: false, needsNewKey: false, lastReport: null });
    return publish('off');
  }
  if (permission.status !== 'granted') {
    await stopAll();
    return publish('needsPermission');
  }

  try {
    if (state.needsNewKey || !(await locationKeyStore.get())) {
      await issueKey();
      await writeState({ needsNewKey: false });
    }
    await startAll(); // idempotent: restarts with the current options
  } catch (error) {
    if (isApiError(error, 'consent.location_required')) {
      await stopAll();
      return publish('needsSharing');
    }
    // Offline: whatever is running keeps running; the next foreground retries.
  }
  return publish('on');
}

/** Runs syncBackgroundLocation on mount, on consent changes and on every return to the foreground. */
export function useBackgroundLocationSync(locationSharing: boolean | undefined): void {
  useEffect(() => {
    if (locationSharing === undefined) return; // profile not loaded yet: do not stop anything prematurely
    void syncBackgroundLocation(locationSharing).catch(() => undefined);
    const subscription = AppState.addEventListener('change', (next) => {
      if (next === 'active') void syncBackgroundLocation(locationSharing).catch(() => undefined);
    });
    return () => subscription.remove();
  }, [locationSharing]);
}

async function issueKey(): Promise<void> {
  const { key } = await meApi.issueLocationKey(await getDeviceId());
  await locationKeyStore.set(key);
}

function publish<T extends BackgroundStatus>(status: T): T {
  useBackgroundLocationStore.setState({ status });
  return status;
}

// Another account may sign in next on this phone: it must opt in itself. The server already revoked the
// key at logout (or ends it with the session), so only local cleanup is needed.
registerSignOutCleanup(async () => {
  await stopAll();
  await locationKeyStore.clear().catch(() => undefined);
  await clearState();
  publish('off');
});
