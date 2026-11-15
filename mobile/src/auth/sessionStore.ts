import { create } from 'zustand';

import type { SessionUser } from '@/api/types';

export type SessionStatus = 'loading' | 'signedOut' | 'signedIn';

export interface SessionState {
  status: SessionStatus;
  user: SessionUser | null;
  /** Why the last session ended (server code such as auth.refresh_reused), shown once on the sign-in screen. */
  endedReason: string | null;
}

/**
 * The signed-in user as far as the UI is concerned. Route guards in src/app read this;
 * src/auth/session.ts is the only writer.
 */
export const useSessionStore = create<SessionState>(() => ({
  status: 'loading',
  user: null,
  endedReason: null,
}));

export const isAdminRole = (user: SessionUser | null): boolean =>
  user?.role === 'SubAdmin' || user?.role === 'MasterAdmin';
