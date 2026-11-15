import * as Location from 'expo-location';
import { useCallback, useEffect, useRef, useState } from 'react';
import { Linking, Platform } from 'react-native';

import { GOOD_ACCURACY_METERS, isFixFresh, pickBetterFix, toFix, type Fix } from './fix';

export type LocationStatus =
  | 'checking' // asking for permission / first fix
  | 'denied' // permission refused
  | 'servicesOff' // device location turned off
  | 'locating' // permission granted, waiting for a fix or improving it
  | 'ready' // accurate enough
  | 'unavailable'; // no fix could be obtained

export interface CurrentLocation {
  status: LocationStatus;
  /** Best fresh fix so far. An SOS can be sent as soon as this exists, even while accuracy improves. */
  fix: Fix | null;
  canAskAgain: boolean;
  retry: () => void;
  openSettings: () => void;
  /** Android only: shows the system "turn on location" dialog. */
  enableServices: () => void;
}

const LAST_KNOWN_MAX_AGE_MS = 2 * 60_000;
const UNAVAILABLE_AFTER_MS = 30_000;

/**
 * Foreground location for raising an SOS: an instant last-known fix when there is a recent one,
 * then a high-accuracy watch that keeps improving it while the screen is open.
 */
export function useCurrentLocation(enabled = true): CurrentLocation {
  const [status, setStatus] = useState<LocationStatus>('checking');
  const [fix, setFix] = useState<Fix | null>(null);
  const [canAskAgain, setCanAskAgain] = useState(true);
  const [attempt, setAttempt] = useState(0);
  const fixRef = useRef<Fix | null>(null);

  useEffect(() => {
    if (!enabled) return;
    let cancelled = false;
    let subscription: Location.LocationSubscription | null = null;
    let giveUpTimer: ReturnType<typeof setTimeout> | null = null;

    const accept = (candidate: Fix | null) => {
      if (cancelled) return;
      const best = pickBetterFix(fixRef.current, candidate);
      if (!best || best === fixRef.current) return;
      fixRef.current = best;
      setFix(best);
      setStatus((best.accuracyMeters ?? Infinity) <= GOOD_ACCURACY_METERS ? 'ready' : 'locating');
    };

    (async () => {
      setStatus('checking');
      let permission = await Location.getForegroundPermissionsAsync();
      if (!permission.granted && permission.canAskAgain) {
        permission = await Location.requestForegroundPermissionsAsync();
      }
      if (cancelled) return;
      setCanAskAgain(permission.canAskAgain);
      if (!permission.granted) {
        setStatus('denied');
        return;
      }
      if (!(await Location.hasServicesEnabledAsync())) {
        if (!cancelled) setStatus('servicesOff');
        return;
      }
      if (cancelled) return;
      setStatus('locating');

      try {
        const last = await Location.getLastKnownPositionAsync({ maxAge: LAST_KNOWN_MAX_AGE_MS, requiredAccuracy: 500 });
        if (last) accept(toFix(last));
      } catch {
        // No cached fix: the watch below provides one.
      }

      giveUpTimer = setTimeout(() => {
        if (!cancelled && !fixRef.current) setStatus('unavailable');
      }, UNAVAILABLE_AFTER_MS);

      subscription = await Location.watchPositionAsync(
        { accuracy: Location.Accuracy.BestForNavigation, timeInterval: 2_000, distanceInterval: 0 },
        (position) => accept(toFix(position)),
        () => {
          if (!cancelled && !fixRef.current) setStatus('unavailable');
        },
      );
      if (cancelled) subscription.remove();
    })().catch(() => {
      if (!cancelled) setStatus(fixRef.current ? 'locating' : 'unavailable');
    });

    return () => {
      cancelled = true;
      subscription?.remove();
      if (giveUpTimer) clearTimeout(giveUpTimer);
    };
  }, [enabled, attempt]);

  // A fix that has aged out (screen left open for a long time) no longer counts.
  const freshFix = fix && isFixFresh(fix) ? fix : null;

  const retry = useCallback(() => setAttempt((a) => a + 1), []);
  const openSettings = useCallback(() => void Linking.openSettings(), []);
  const enableServices = useCallback(() => {
    if (Platform.OS !== 'android') {
      void Linking.openSettings();
      return;
    }
    Location.enableNetworkProviderAsync()
      .then(() => setAttempt((a) => a + 1))
      .catch(() => {
        // The user dismissed the dialog.
      });
  }, []);

  return { status, fix: freshFix, canAskAgain, retry, openSettings, enableServices };
}

/** One-off fix for non-urgent use (e.g. after granting LocationSharing consent). Null if unavailable. */
export async function getOneFix(): Promise<Fix | null> {
  const permission = await Location.getForegroundPermissionsAsync();
  if (!permission.granted) return null;
  const last = await Location.getLastKnownPositionAsync({ maxAge: 5 * 60_000, requiredAccuracy: 500 });
  if (last) return toFix(last);
  const current = await Location.getCurrentPositionAsync({ accuracy: Location.Accuracy.Balanced });
  return toFix(current);
}
