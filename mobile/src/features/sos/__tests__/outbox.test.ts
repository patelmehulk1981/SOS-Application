import { ApiError, NetworkError } from '@/api/errors';
import type { SosDetail } from '@/api/types';
import { CREATE_DEADLINE_MS, PHOTO_DEADLINE_MS, isPermanentFailure, processPending, retryDelayMs, type OutboxDeps, type PendingSos } from '../outbox';

const NOW = Date.parse('2026-11-15T10:00:00Z');

function pending(overrides: Partial<PendingSos> = {}): PendingSos {
  return {
    clientRequestId: 'c1',
    queuedAt: new Date(NOW - 5_000).toISOString(),
    photoUris: [],
    request: {
      clientRequestId: 'c1',
      category: 'Medical',
      latitude: 23.0225,
      longitude: 72.5714,
      accuracyMeters: 8,
      capturedAt: new Date(NOW - 10_000).toISOString(),
    },
    ...overrides,
  };
}

const sos = (photos = 0) => ({ id: 'sos-1', photos: Array.from({ length: photos }, (_, i) => ({ id: `p${i}` })) }) as unknown as SosDetail;

function setup() {
  const deps = {
    save: jest.fn(async () => {}),
    clear: jest.fn(async () => {}),
    create: jest.fn(),
    uploadPhotos: jest.fn(),
    deletePhotos: jest.fn(),
    now: () => NOW,
  } satisfies OutboxDeps;
  return deps;
}

describe('processPending', () => {
  it('creates the SOS and finishes when there are no photos', async () => {
    const deps = setup();
    deps.create.mockResolvedValueOnce(sos());

    const outcome = await processPending(pending(), deps);

    expect(outcome).toEqual({ kind: 'sent', sosId: 'sos-1', sos: sos() });
    expect(deps.create).toHaveBeenCalledWith(pending().request);
    expect(deps.clear).toHaveBeenCalled();
  });

  it('persists the SOS id before uploading photos, so a crash never re-creates it', async () => {
    const deps = setup();
    deps.create.mockResolvedValueOnce(sos());
    deps.uploadPhotos.mockResolvedValueOnce(sos(2));

    const outcome = await processPending(pending({ photoUris: ['a.jpg', 'b.jpg'] }), deps);

    expect(deps.save).toHaveBeenCalledWith(expect.objectContaining({ sosId: 'sos-1' }));
    expect(deps.save.mock.invocationCallOrder[0]!).toBeLessThan(deps.uploadPhotos.mock.invocationCallOrder[0]!);
    expect(deps.uploadPhotos).toHaveBeenCalledWith('sos-1', ['a.jpg', 'b.jpg']);
    expect(outcome).toMatchObject({ kind: 'sent', sosId: 'sos-1' });
    expect(deps.deletePhotos).toHaveBeenCalledWith(['a.jpg', 'b.jpg']);
  });

  it('keeps everything for a retry when offline (same clientRequestId will be replayed)', async () => {
    const deps = setup();
    deps.create.mockRejectedValueOnce(new NetworkError('offline', false));

    const outcome = await processPending(pending({ photoUris: ['a.jpg'] }), deps);

    expect(outcome.kind).toBe('retryLater');
    expect(deps.clear).not.toHaveBeenCalled();
    expect(deps.deletePhotos).not.toHaveBeenCalled();
  });

  it.each([500, 503, 429])('retries on HTTP %i', async (status) => {
    const deps = setup();
    deps.create.mockRejectedValueOnce(new ApiError(status, {}));
    await expect(processPending(pending(), deps)).resolves.toMatchObject({ kind: 'retryLater' });
  });

  it('gives up on a permanent refusal and cleans up', async () => {
    const deps = setup();
    deps.create.mockRejectedValueOnce(new ApiError(422, { code: 'sos.no_admin' }));

    const outcome = await processPending(pending({ photoUris: ['a.jpg'] }), deps);

    expect(outcome).toMatchObject({ kind: 'rejected' });
    expect(deps.clear).toHaveBeenCalled();
    expect(deps.deletePhotos).toHaveBeenCalledWith(['a.jpg']);
  });

  it('reports the already-open SOS id from the problem body', async () => {
    const deps = setup();
    deps.create.mockRejectedValueOnce(new ApiError(409, { code: 'sos.already_open', sosId: 'open-1' }));
    await expect(processPending(pending(), deps)).resolves.toEqual({ kind: 'alreadyOpen', sosId: 'open-1' });
  });

  it('drops an undelivered SOS once its location would be too old for the API', async () => {
    const deps = setup();
    const outcome = await processPending(pending({ queuedAt: new Date(NOW - CREATE_DEADLINE_MS - 1).toISOString() }), deps);
    expect(outcome).toEqual({ kind: 'expired' });
    expect(deps.create).not.toHaveBeenCalled();
    expect(deps.clear).toHaveBeenCalled();
  });

  it('resumes with photos only when the SOS already exists', async () => {
    const deps = setup();
    deps.uploadPhotos.mockResolvedValueOnce(sos(1));

    const outcome = await processPending(pending({ sosId: 'sos-1', photoUris: ['a.jpg'] }), deps);

    expect(deps.create).not.toHaveBeenCalled();
    expect(outcome).toMatchObject({ kind: 'sent', sosId: 'sos-1' });
  });

  it('keeps photos pending on a network failure after creation', async () => {
    const deps = setup();
    deps.create.mockResolvedValueOnce(sos());
    deps.uploadPhotos.mockRejectedValueOnce(new NetworkError('offline', false));

    const outcome = await processPending(pending({ photoUris: ['a.jpg'] }), deps);

    expect(outcome).toMatchObject({ kind: 'photosPending', sosId: 'sos-1' });
    expect(deps.deletePhotos).not.toHaveBeenCalled();
  });

  it('a refused photo never turns a delivered SOS into a failure (also when resuming)', async () => {
    const deps = setup();
    deps.uploadPhotos.mockRejectedValueOnce(new ApiError(422, { code: 'sos.closed' }));

    const outcome = await processPending(pending({ sosId: 'sos-1', photoUris: ['a.jpg'] }), deps);

    expect(outcome).toMatchObject({ kind: 'sent', sosId: 'sos-1' });
    expect(outcome.kind === 'sent' && outcome.photoError).toBeTruthy();
    expect(deps.deletePhotos).toHaveBeenCalledWith(['a.jpg']);
  });

  it('stops retrying photos after the photo deadline', async () => {
    const deps = setup();
    const outcome = await processPending(
      pending({ sosId: 'sos-1', photoUris: ['a.jpg'], queuedAt: new Date(NOW - PHOTO_DEADLINE_MS - 1).toISOString() }),
      deps,
    );
    expect(outcome).toMatchObject({ kind: 'sent', sosId: 'sos-1', photoError: 'expired' });
    expect(deps.uploadPhotos).not.toHaveBeenCalled();
  });
});

describe('retry policy', () => {
  it('classifies errors', () => {
    expect(isPermanentFailure(new NetworkError('x', true))).toBe(false);
    expect(isPermanentFailure(new ApiError(408, {}))).toBe(false);
    expect(isPermanentFailure(new ApiError(429, {}))).toBe(false);
    expect(isPermanentFailure(new ApiError(502, {}))).toBe(false);
    expect(isPermanentFailure(new ApiError(400, {}))).toBe(true);
    expect(isPermanentFailure(new ApiError(422, {}))).toBe(true);
  });

  it('backs off exponentially up to a minute', () => {
    expect([1, 2, 3, 4, 5, 6, 10].map(retryDelayMs)).toEqual([3_000, 6_000, 12_000, 24_000, 48_000, 60_000, 60_000]);
  });
});
