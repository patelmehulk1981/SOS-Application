/**
 * Background task definitions. Side-effect module imported by index.ts before Expo Router: when the
 * OS starts the app only to run a task (iOS geofence relaunch, Android WorkManager), no component
 * mounts, so tasks must be defined at module load.
 */
import * as BackgroundTask from 'expo-background-task';
import { GeofencingEventType, type LocationObject } from 'expo-location';
import * as TaskManager from 'expo-task-manager';

import { currentPosition } from './native';
import { BackgroundTasks, pickReportableFix } from './policy';
import { reportFix } from './reporter';

async function reportLatest(positions: readonly LocationObject[]): Promise<void> {
  const fix = pickReportableFix(positions, Date.now());
  if (fix) await reportFix(fix);
}

async function reportCurrent(): Promise<void> {
  const position = await currentPosition();
  if (position) await reportLatest([position]);
}

TaskManager.defineTask<{ locations?: LocationObject[] }>(BackgroundTasks.updates, async ({ data, error }) => {
  if (error || !data?.locations?.length) return;
  try {
    await reportLatest(data.locations);
  } catch {
    // Never throw out of a task; the next batch tries again.
  }
});

TaskManager.defineTask<{ eventType?: GeofencingEventType }>(BackgroundTasks.geofence, async ({ data, error }) => {
  if (error || data?.eventType !== GeofencingEventType.Exit) return;
  try {
    await reportCurrent(); // a successful report also moves the circle to the new position
  } catch {
    // Best effort.
  }
});

TaskManager.defineTask(BackgroundTasks.checkIn, async () => {
  try {
    await reportCurrent(); // throttled: sends only when the keep-alive is due or the phone has moved
    return BackgroundTask.BackgroundTaskResult.Success;
  } catch {
    return BackgroundTask.BackgroundTaskResult.Failed;
  }
});
