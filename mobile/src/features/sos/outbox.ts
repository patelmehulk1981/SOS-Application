import { ApiError, NetworkError, SessionExpiredError } from '@/api/errors';
import type { CreateSosRequest, SosDetail } from '@/api/types';

/**
 * An SOS that has been pressed but not fully delivered yet. Persisted, so neither a dropped
 * connection nor the app being killed loses it (docs/04b-mobile-sos.md, "Delivery").
 *
 * Phase 1 creates the SOS (POST /sos, idempotent through clientRequestId).
 * Phase 2 uploads the photos (POST /sos/{id}/photos); the emergency is never held back by them.
 */
export interface PendingSos {
  clientRequestId: string;
  request: CreateSosRequest;
  /** Local photo files still to upload. */
  photoUris: string[];
  /** Set once phase 1 succeeded. */
  sosId?: string;
  queuedAt: string;
}

export type OutboxOutcome =
  /**
   * The SOS is delivered; nothing is left to retry. `sos` is the latest response when this run made one.
   * `photoError` is set when the photos had to be dropped (refused, SOS already closed, or too old).
   */
  | { kind: 'sent'; sosId: string; sos?: SosDetail; photoError?: unknown }
  /** The SOS exists; photos will be retried. */
  | { kind: 'photosPending'; sosId: string; error: unknown }
  /** Not delivered yet; will be retried. */
  | { kind: 'retryLater'; error: unknown }
  /** The person already has an open SOS (409 sos.already_open). */
  | { kind: 'alreadyOpen'; sosId?: string }
  /** The API refused it for good (validation, no admin, stale fix, …). */
  | { kind: 'rejected'; error: unknown }
  /** Too old to send: the location would be rejected as stale. */
  | { kind: 'expired' };

export interface OutboxDeps {
  save: (pending: PendingSos) => Promise<void>;
  clear: () => Promise<void>;
  create: (request: CreateSosRequest) => Promise<SosDetail>;
  uploadPhotos: (sosId: string, photoUris: string[]) => Promise<SosDetail>;
  deletePhotos: (uris: readonly string[]) => void;
  now?: () => number;
}

/** The API accepts fixes up to 30 minutes old; an undelivered SOS older than this is dropped. */
export const CREATE_DEADLINE_MS = 28 * 60_000;
/** Photos for an SOS that was created are retried for this long. */
export const PHOTO_DEADLINE_MS = 6 * 60 * 60_000;

/** Errors that retrying cannot fix. Everything else (offline, timeout, 5xx, 429) is retried. */
export function isPermanentFailure(error: unknown): boolean {
  if (error instanceof SessionExpiredError) return true;
  if (error instanceof NetworkError) return false;
  if (error instanceof ApiError) return error.status >= 400 && error.status < 500 && error.status !== 408 && error.status !== 429;
  return false;
}

export async function processPending(pending: PendingSos, deps: OutboxDeps): Promise<OutboxOutcome> {
  const now = deps.now?.() ?? Date.now();
  const age = now - Date.parse(pending.queuedAt);
  let current = pending;
  let created: SosDetail | undefined;

  // ---- Phase 1: create -------------------------------------------------------------------
  if (!current.sosId) {
    if (age > CREATE_DEADLINE_MS) {
      await finish(current, deps);
      return { kind: 'expired' };
    }
    try {
      created = await deps.create(current.request);
    } catch (error) {
      if (error instanceof ApiError && error.code === 'sos.already_open') {
        await finish(current, deps);
        const sosId = typeof error.problem.sosId === 'string' ? error.problem.sosId : undefined;
        return { kind: 'alreadyOpen', sosId };
      }
      if (isPermanentFailure(error)) {
        await finish(current, deps);
        return { kind: 'rejected', error };
      }
      return { kind: 'retryLater', error };
    }
    current = { ...current, sosId: created.id };
    if (current.photoUris.length > 0) await deps.save(current);
  }

  const sosId = current.sosId!;

  // ---- Phase 2: photos -------------------------------------------------------------------
  if (current.photoUris.length === 0) {
    await finish(current, deps);
    return { kind: 'sent', sosId, sos: created };
  }

  if (age > PHOTO_DEADLINE_MS) {
    await finish(current, deps);
    return { kind: 'sent', sosId, sos: created, photoError: 'expired' };
  }

  try {
    const withPhotos = await deps.uploadPhotos(sosId, current.photoUris);
    await finish(current, deps);
    return { kind: 'sent', sosId, sos: withPhotos };
  } catch (error) {
    if (isPermanentFailure(error)) {
      // e.g. the SOS was closed meanwhile, or an image was refused: the SOS itself stands.
      await finish(current, deps);
      return { kind: 'sent', sosId, sos: created, photoError: error };
    }
    return { kind: 'photosPending', sosId, error };
  }
}

async function finish(pending: PendingSos, deps: OutboxDeps): Promise<void> {
  deps.deletePhotos(pending.photoUris);
  await deps.clear();
}

/** Delay before retry n (1-based): 3 s, 6 s, 12 s, … capped at 60 s. */
export function retryDelayMs(attempt: number): number {
  return Math.min(60_000, 3_000 * 2 ** Math.max(0, attempt - 1));
}
