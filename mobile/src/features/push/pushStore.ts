import { create } from 'zustand';

import type { PushPayload } from './payload';

/**
 * unavailable  - no Firebase in this build (development without config files): polling only
 * undetermined - never asked; the home screen offers to turn notifications on
 * denied       - refused (or blocked in system settings): the home screen links to Settings
 * granted      - allowed
 */
export type PushPermission = 'unavailable' | 'undetermined' | 'denied' | 'granted';

export interface InAppNotice {
  id: number;
  title: string;
  body?: string;
  payload: PushPayload;
  urgent: boolean;
}

interface PushState {
  permission: PushPermission;
  /** The API has this phone's installation id for the current user: pushes will arrive. */
  registered: boolean;
  /** A push received while the app was open (the OS does not show those). */
  notice: InAppNotice | null;
  /** A notification the user tapped, waiting for the signed-in area to open it. */
  pendingOpen: PushPayload | null;
}

export const usePushStore = create<PushState>(() => ({
  permission: 'undetermined',
  registered: false,
  notice: null,
  pendingOpen: null,
}));

/** Push can be relied on to report changes, so polling can slow down. */
export const isPushActive = (): boolean => {
  const { permission, registered } = usePushStore.getState();
  return permission === 'granted' && registered;
};
