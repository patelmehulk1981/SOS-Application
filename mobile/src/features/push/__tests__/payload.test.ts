import { PUSH_TYPES, isUrgent, parsePushPayload, routeFor, staleQueriesFor } from '../payload';

const SOS_ID = '0193a3c4-5d6e-7f80-9a1b-2c3d4e5f6a7b';

describe('parsePushPayload', () => {
  it('accepts every message type the API sends', () => {
    for (const type of PUSH_TYPES) {
      expect(parsePushPayload({ type, sosId: SOS_ID, referenceNo: 'SOS-00000123' })).toEqual({
        type,
        sosId: SOS_ID,
        referenceNo: 'SOS-00000123',
      });
    }
  });

  it('ignores anything unexpected instead of navigating to it', () => {
    expect(parsePushPayload(undefined)).toBeNull();
    expect(parsePushPayload('sos.alert')).toBeNull();
    expect(parsePushPayload({ type: 'sos.unknown', sosId: SOS_ID })).toBeNull();
    expect(parsePushPayload({ type: 'sos.alert', sosId: '../admin' })).toBeNull();
    expect(parsePushPayload({ type: 'sos.alert' })).toBeNull();
    expect(parsePushPayload({ type: 'sos.alert', sosId: SOS_ID, referenceNo: 42 })).toEqual({
      type: 'sos.alert',
      sosId: SOS_ID,
      referenceNo: undefined,
    });
  });
});

describe('routeFor', () => {
  it('opens the SOS for every type except closure, which opens the alerts feed', () => {
    expect(routeFor({ type: 'sos.alert', sosId: SOS_ID })).toEqual({ pathname: '/sos/[id]', params: { id: SOS_ID } });
    expect(routeFor({ type: 'sos.review_requested', sosId: SOS_ID })).toEqual({ pathname: '/sos/[id]', params: { id: SOS_ID } });
    expect(routeFor({ type: 'sos.closed', sosId: SOS_ID })).toEqual({ pathname: '/alerts' });
  });
});

describe('staleQueriesFor', () => {
  it('refreshes the SOS plus the list that the push concerns', () => {
    expect(staleQueriesFor({ type: 'sos.escalated', sosId: SOS_ID })).toEqual([['sos', 'detail', SOS_ID], ['admin', 'sos']]);
    expect(staleQueriesFor({ type: 'sos.approved', sosId: SOS_ID })).toEqual([['sos', 'detail', SOS_ID], ['sos', 'mine']]);
    expect(staleQueriesFor({ type: 'sos.alert', sosId: SOS_ID })).toEqual([['sos', 'detail', SOS_ID], ['sos', 'alerts']]);
  });
});

describe('isUrgent', () => {
  it('keeps emergencies on screen and lets status updates fade', () => {
    expect(isUrgent('sos.alert')).toBe(true);
    expect(isUrgent('sos.escalated')).toBe(true);
    expect(isUrgent('sos.approved')).toBe(false);
    expect(isUrgent('sos.closed')).toBe(false);
  });
});
