import * as BackgroundTask from 'expo-background-task';
import * as Location from 'expo-location';
import * as TaskManager from 'expo-task-manager';

import { BackgroundTasks, CHECKIN_INTERVAL_MINUTES, MIN_MOVE_METERS, geofenceAround } from './policy';

/**
 * Thin wrappers over the native background APIs. Every call is defensive: a missing native module
 * (Expo Go, web, a simulator without BGTaskScheduler) must never crash the app or a task.
 */

export type BackgroundPermission = 'granted' | 'foregroundOnly' | 'undetermined' | 'denied';

export interface PermissionState {
  status: BackgroundPermission;
  /** The system prompt can still be shown; otherwise only Settings can change it. */
  canAsk: boolean;
}

export async function getPermission(): Promise<PermissionState> {
  try {
    const foreground = await Location.getForegroundPermissionsAsync();
    if (!foreground.granted) {
      return { status: foreground.canAskAgain ? 'undetermined' : 'denied', canAsk: foreground.canAskAgain };
    }
    const background = await Location.getBackgroundPermissionsAsync();
    if (background.granted) return { status: 'granted', canAsk: false };
    return { status: 'foregroundOnly', canAsk: background.canAskAgain };
  } catch {
    return { status: 'denied', canAsk: false };
  }
}

/**
 * Foreground first (required), then background. Android 11+ shows the app's location settings page
 * instead of a dialog ("Allow all the time"); iOS offers "Change to Always Allow" once.
 */
export async function requestPermission(): Promise<PermissionState> {
  try {
    let foreground = await Location.getForegroundPermissionsAsync();
    if (!foreground.granted && foreground.canAskAgain) foreground = await Location.requestForegroundPermissionsAsync();
    if (!foreground.granted) return { status: foreground.canAskAgain ? 'undetermined' : 'denied', canAsk: foreground.canAskAgain };
    await Location.requestBackgroundPermissionsAsync();
  } catch {
    // Fall through to the actual state.
  }
  return getPermission();
}

export async function isAvailable(): Promise<boolean> {
  try {
    return (await TaskManager.isAvailableAsync()) && (await Location.isBackgroundLocationAvailableAsync());
  } catch {
    return false;
  }
}

/**
 * Starts (or restarts with current options) the three background mechanisms:
 * - movement updates: network/Wi-Fi accuracy, only after ~250 m, batched by the OS, paused by iOS when still;
 * - the geofence, once a first report gives it a centre (see recenterGeofence);
 * - the hourly-at-best check-in, so a stationary phone keeps its location under 12 hours old.
 * No Android foreground service: no permanent notification; Android delivers background fixes a few times an hour.
 */
export async function startAll(): Promise<void> {
  await Location.startLocationUpdatesAsync(BackgroundTasks.updates, {
    accuracy: Location.Accuracy.Balanced,
    distanceInterval: MIN_MOVE_METERS,
    timeInterval: 15 * 60_000, // Android: requested interval; the OS throttles background apps further
    deferredUpdatesDistance: MIN_MOVE_METERS,
    deferredUpdatesInterval: 5 * 60_000,
    pausesUpdatesAutomatically: true,
    activityType: Location.ActivityType.Other,
    showsBackgroundLocationIndicator: false,
    mayShowUserSettingsDialog: false,
  });
  try {
    await BackgroundTask.registerTaskAsync(BackgroundTasks.checkIn, { minimumInterval: CHECKIN_INTERVAL_MINUTES });
  } catch {
    // Not available (iOS Simulator): movement updates and the geofence still work.
  }
}

export async function stopAll(): Promise<void> {
  await Promise.allSettled([
    (async () => {
      if (await Location.hasStartedLocationUpdatesAsync(BackgroundTasks.updates)) {
        await Location.stopLocationUpdatesAsync(BackgroundTasks.updates);
      }
    })(),
    (async () => {
      if (await Location.hasStartedGeofencingAsync(BackgroundTasks.geofence)) {
        await Location.stopGeofencingAsync(BackgroundTasks.geofence);
      }
    })(),
    (async () => {
      if (await TaskManager.isTaskRegisteredAsync(BackgroundTasks.checkIn)) {
        await BackgroundTask.unregisterTaskAsync(BackgroundTasks.checkIn);
      }
    })(),
  ]);
}

export async function isRunning(): Promise<boolean> {
  try {
    return await Location.hasStartedLocationUpdatesAsync(BackgroundTasks.updates);
  } catch {
    return false;
  }
}

/** Moves the wake-up circle to the latest report (calling again replaces the region). */
export async function recenterGeofence(point: { latitude: number; longitude: number }): Promise<void> {
  try {
    await Location.startGeofencingAsync(BackgroundTasks.geofence, [geofenceAround(point)]);
  } catch {
    // Permission changed meanwhile: the next app open re-evaluates.
  }
}

/** A fresh-enough fix inside a background task: the OS cache first, then one network-accuracy reading. */
export async function currentPosition(): Promise<Location.LocationObject | null> {
  try {
    const last = await Location.getLastKnownPositionAsync({ maxAge: 10 * 60_000, requiredAccuracy: 500 });
    if (last) return last;
    return await Location.getCurrentPositionAsync({ accuracy: Location.Accuracy.Balanced });
  } catch {
    return null;
  }
}
