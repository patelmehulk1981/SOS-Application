import AsyncStorage from '@react-native-async-storage/async-storage';
import { AppState } from 'react-native';
import { create } from 'zustand';

import { sosApi } from '@/api/endpoints';
import { queryClient } from '@/api/queryClient';
import { registerSignOutCleanup } from '@/auth/session';
import { processPending, retryDelayMs, type OutboxOutcome, type PendingSos } from './outbox';
import { deletePhotos, toUploadFile } from './photos';
import { sosKeys } from './queries';

/**
 * Persisted at most until delivery (minutes). App-sandboxed storage protected by device encryption;
 * removed on delivery, expiry and sign-out.
 */
const STORAGE_KEY = 'sos.outbox.v1';

interface OutboxState {
  pending: PendingSos | null;
  sending: boolean;
  attempt: number;
  lastOutcome: OutboxOutcome | null;
}

export const useSosOutbox = create<OutboxState>(() => ({
  pending: null,
  sending: false,
  attempt: 0,
  lastOutcome: null,
}));

let inFlight: Promise<OutboxOutcome> | null = null;
let retryTimer: ReturnType<typeof setTimeout> | null = null;
let loaded = false;

const deps = {
  save: (pending: PendingSos) => AsyncStorage.setItem(STORAGE_KEY, JSON.stringify(pending)),
  clear: () => AsyncStorage.removeItem(STORAGE_KEY),
  create: sosApi.create,
  uploadPhotos: (sosId: string, uris: string[]) => sosApi.uploadPhotos(sosId, uris.map(toUploadFile)),
  deletePhotos,
};

/** Queues a new SOS and sends it immediately. Resolves with the first attempt's outcome. */
export async function submitSos(pending: PendingSos): Promise<OutboxOutcome> {
  cancelRetry();
  await deps.save(pending);
  useSosOutbox.setState({ pending, attempt: 0, lastOutcome: null });
  return flushOutbox();
}

/** Sends whatever is pending (single-flight). Safe to call any time. */
export function flushOutbox(): Promise<OutboxOutcome> {
  if (inFlight) return inFlight;
  const pending = useSosOutbox.getState().pending;
  if (!pending) return Promise.resolve({ kind: 'expired' } as const);

  cancelRetry();
  useSosOutbox.setState((s) => ({ sending: true, attempt: s.attempt + 1 }));
  inFlight = processPending(pending, {
    ...deps,
    // Phase 1 succeeded: remember the id at once, so a crash in phase 2 never re-creates (and the UI can move on).
    save: async (next) => {
      await deps.save(next);
      useSosOutbox.setState({ pending: next });
    },
  })
    .then((outcome) => {
      applyOutcome(outcome);
      return outcome;
    })
    .finally(() => {
      inFlight = null;
    });
  return inFlight;
}

/** Loads a pending SOS left over from a previous run (app killed or offline) and resumes delivery. */
export async function resumeOutbox(): Promise<void> {
  if (!loaded) {
    loaded = true;
    const raw = await AsyncStorage.getItem(STORAGE_KEY);
    if (raw) {
      try {
        useSosOutbox.setState({ pending: JSON.parse(raw) as PendingSos, attempt: 0 });
      } catch {
        await AsyncStorage.removeItem(STORAGE_KEY);
      }
    }
  }
  if (useSosOutbox.getState().pending && !inFlight) await flushOutbox();
}

/** Drops anything pending (sign-out / account deletion). */
export async function discardOutbox(): Promise<void> {
  cancelRetry();
  const pending = useSosOutbox.getState().pending;
  if (pending) deletePhotos(pending.photoUris);
  await AsyncStorage.removeItem(STORAGE_KEY);
  useSosOutbox.setState({ pending: null, sending: false, attempt: 0, lastOutcome: null });
}

function applyOutcome(outcome: OutboxOutcome): void {
  const retry = outcome.kind === 'retryLater' || outcome.kind === 'photosPending';
  useSosOutbox.setState((s) => ({
    sending: false,
    lastOutcome: outcome,
    pending: retry ? s.pending : null,
  }));

  if (outcome.kind === 'sent' && outcome.sos) queryClient.setQueryData(sosKeys.detail(outcome.sosId), outcome.sos);
  if (outcome.kind === 'sent' || outcome.kind === 'alreadyOpen' || outcome.kind === 'photosPending') {
    void queryClient.invalidateQueries({ queryKey: sosKeys.all });
  }

  if (retry) {
    const delay = retryDelayMs(useSosOutbox.getState().attempt);
    retryTimer = setTimeout(() => void flushOutbox(), delay);
  }
}

function cancelRetry(): void {
  if (retryTimer) clearTimeout(retryTimer);
  retryTimer = null;
}

// Coming back to the foreground (often: connectivity restored) retries at once instead of waiting for the timer.
AppState.addEventListener('change', (state) => {
  if (state === 'active' && useSosOutbox.getState().pending && !inFlight) void flushOutbox();
});

registerSignOutCleanup(discardOutbox);
