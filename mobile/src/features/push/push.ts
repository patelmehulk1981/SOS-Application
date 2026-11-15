import type { Messaging, RemoteMessage } from '@react-native-firebase/messaging';
import * as Notifications from 'expo-notifications';
import { AppState, Linking } from 'react-native';

import { meApi } from '@/api/endpoints';
import { queryClient } from '@/api/queryClient';
import { registerSignOutCleanup } from '@/auth/session';
import { env } from '@/config/env';
import { appVersion, deviceModel, platform } from '@/lib/device';
import { pushInstallation } from '@/lib/pushInstallation';

import { ensureNotificationChannels } from './channels';
import { isUrgent, parsePushPayload, staleQueriesFor } from './payload';
import { usePushStore, type PushPermission } from './pushStore';

/**
 * Push notifications through Firebase Cloud Messaging, addressed by Firebase Installation ID (FID):
 * permission → messaging.register() → onRegistered(fid) → POST /me/devices { installationId }.
 * Firebase is loaded lazily so development builds without Firebase config still run (they poll instead).
 *
 * Background and killed-app notifications are shown by the OS from the message's notification part; this
 * module handles taps (opening the SOS) and messages that arrive while the app is open (in-app notice).
 */

type MessagingModule = typeof import('@react-native-firebase/messaging');

function loadMessaging(): { mod: MessagingModule; messaging: Messaging } | null {
  if (!env.pushAvailable) return null;
  try {
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    const mod = require('@react-native-firebase/messaging') as MessagingModule;
    return { mod, messaging: mod.getMessaging() };
  } catch {
    return null; // native module missing (e.g. Expo Go)
  }
}

function toPermission(status: Notifications.NotificationPermissionsStatus): PushPermission {
  if (status.granted) return 'granted';
  return status.canAskAgain ? 'undetermined' : 'denied';
}

/** Reads the OS permission without prompting. */
export async function refreshPushPermission(): Promise<PushPermission> {
  if (!env.pushAvailable) {
    usePushStore.setState({ permission: 'unavailable' });
    return 'unavailable';
  }
  const permission = toPermission(await Notifications.getPermissionsAsync());
  usePushStore.setState({ permission });
  return permission;
}

/**
 * Asks for notification permission (Android 13+ / iOS). If the user refused before, the OS will not show the
 * prompt again, so Settings is opened instead.
 */
export async function requestPushPermission(): Promise<PushPermission> {
  if (!env.pushAvailable) return 'unavailable';
  const current = await Notifications.getPermissionsAsync();
  if (!current.granted && !current.canAskAgain) {
    await Linking.openSettings();
    return 'denied';
  }
  const permission = toPermission(
    await Notifications.requestPermissionsAsync({ ios: { allowAlert: true, allowSound: true, allowBadge: true } }),
  );
  usePushStore.setState({ permission });
  if (permission === 'granted') await registerForPush();
  return permission;
}

let unsubscribeRegistered: (() => void) | null = null;
/** FID the API has confirmed for the signed-in user (re-sent after a failure, a new FID or a new user). */
let confirmed: string | null = null;

async function registerForPush(): Promise<void> {
  const firebase = loadMessaging();
  if (!firebase) return;
  const { mod, messaging } = firebase;

  // onRegistered replays the cached FID to a new subscriber, and fires again when the FID rotates.
  unsubscribeRegistered ??= mod.onRegistered(messaging, (fid: string) => {
    pushInstallation.set(fid);
    void sendInstallationId();
  });
  try {
    await mod.register(messaging);
  } catch {
    // No network or Play services: retried on the next foreground.
  }
}

async function sendInstallationId(): Promise<void> {
  const fid = pushInstallation.get();
  if (!fid || fid === confirmed) return;
  try {
    await meApi.registerDevice({ installationId: fid, platform, appVersion, deviceModel });
    confirmed = fid;
    usePushStore.setState({ registered: true });
  } catch {
    // Offline or 5xx: retried on the next foreground (polling keeps working meanwhile).
  }
}

let initialNotificationHandled = false;
let noticeSeq = 0;

function onForegroundMessage(message: RemoteMessage): void {
  const payload = parsePushPayload(message.data);
  if (!payload) return;
  for (const queryKey of staleQueriesFor(payload)) void queryClient.invalidateQueries({ queryKey });
  usePushStore.setState({
    notice: {
      id: ++noticeSeq,
      title: message.notification?.title ?? payload.referenceNo ?? 'Community SOS',
      body: message.notification?.body,
      payload,
      urgent: isUrgent(payload.type),
    },
  });
}

function onOpened(message: RemoteMessage | null): void {
  const payload = message ? parsePushPayload(message.data) : null;
  if (payload) usePushStore.setState({ pendingOpen: payload });
}

/**
 * Starts push for the signed-in user. Called by the signed-in layout; returns a cleanup for unmount
 * (sign-out). Safe without Firebase: it then only records that push is unavailable.
 */
export function startPush(): () => void {
  const subscriptions: (() => void)[] = [];
  const firebase = loadMessaging();

  void (async () => {
    const permission = await refreshPushPermission().catch(() => 'unavailable' as const);
    if (!firebase) return;
    await ensureNotificationChannels().catch(() => undefined);
    if (permission === 'granted') await registerForPush();

    if (!initialNotificationHandled) {
      initialNotificationHandled = true; // only the notification that launched the app, once
      onOpened(await firebase.mod.getInitialNotification(firebase.messaging).catch(() => null));
    }
  })();

  if (firebase) {
    subscriptions.push(firebase.mod.onMessage(firebase.messaging, onForegroundMessage));
    subscriptions.push(firebase.mod.onNotificationOpenedApp(firebase.messaging, onOpened));
  }

  // Back from Settings, or back online: pick up a changed permission and finish a failed registration.
  const appState = AppState.addEventListener('change', (state) => {
    if (state !== 'active') return;
    void refreshPushPermission()
      .then((permission) => (permission === 'granted' ? registerForPush().then(sendInstallationId) : undefined))
      .catch(() => undefined);
  });
  subscriptions.push(() => appState.remove());

  return () => subscriptions.forEach((unsubscribe) => unsubscribe());
}

// The next account on this phone must register again; the server deactivated this phone at logout.
registerSignOutCleanup(() => {
  confirmed = null;
  unsubscribeRegistered?.();
  unsubscribeRegistered = null;
  usePushStore.setState({ registered: false, notice: null, pendingOpen: null });
});

/** Background messages: nothing to do (the OS shows them), but Firebase warns if no handler is set. */
export function setBackgroundHandler(): void {
  const firebase = loadMessaging();
  firebase?.mod.setBackgroundMessageHandler(firebase.messaging, async () => undefined);
}
