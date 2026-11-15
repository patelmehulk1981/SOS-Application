import type { QueryKey } from '@tanstack/react-query';

/**
 * Push payloads sent by the API (backend PushMessages.cs). The `data` part is always
 * `{ type, sosId, referenceNo }`; the lock-screen text never contains personal data, so the app
 * fetches details after the user opens it. Pure functions, unit-tested.
 */

export const PUSH_TYPES = [
  'sos.review_requested',
  'sos.escalated',
  'sos.auto_approved',
  'sos.approved',
  'sos.rejected',
  'sos.alert',
  'sos.responder_update',
  'sos.closed',
] as const;

export type PushType = (typeof PUSH_TYPES)[number];

export interface PushPayload {
  type: PushType;
  sosId: string;
  referenceNo?: string;
}

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Validates untrusted message data; anything unexpected is ignored rather than navigated to. */
export function parsePushPayload(data: unknown): PushPayload | null {
  if (!data || typeof data !== 'object') return null;
  const { type, sosId, referenceNo } = data as Record<string, unknown>;
  if (typeof type !== 'string' || !(PUSH_TYPES as readonly string[]).includes(type)) return null;
  if (typeof sosId !== 'string' || !UUID.test(sosId)) return null;
  return {
    type: type as PushType,
    sosId,
    referenceNo: typeof referenceNo === 'string' && referenceNo.length <= 32 ? referenceNo : undefined,
  };
}

/**
 * Where a tapped notification leads. Every type opens the SOS: admins get the review view, requesters their
 * status, neighbours the help view. A closed SOS is not visible to neighbours, so `sos.closed` opens the alerts feed.
 */
export function routeFor(payload: PushPayload): { pathname: '/sos/[id]'; params: { id: string } } | { pathname: '/alerts' } {
  return payload.type === 'sos.closed' ? { pathname: '/alerts' } : { pathname: '/sos/[id]', params: { id: payload.sosId } };
}

/** Queries made stale by a push, refreshed immediately when it arrives in the foreground. */
export function staleQueriesFor(payload: PushPayload): QueryKey[] {
  const keys: QueryKey[] = [['sos', 'detail', payload.sosId]];
  switch (payload.type) {
    case 'sos.review_requested':
    case 'sos.escalated':
    case 'sos.auto_approved':
      keys.push(['admin', 'sos']);
      break;
    case 'sos.approved':
    case 'sos.rejected':
    case 'sos.responder_update':
      keys.push(['sos', 'mine']);
      break;
    case 'sos.alert':
    case 'sos.closed':
      keys.push(['sos', 'alerts']);
      break;
  }
  return keys;
}

/** Emergencies stay on screen until dismissed; status updates fade after a few seconds. */
export function isUrgent(type: PushType): boolean {
  return type === 'sos.alert' || type === 'sos.review_requested' || type === 'sos.escalated';
}
