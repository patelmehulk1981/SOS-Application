import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { sosApi } from '@/api/endpoints';
import type { ResponseType, SosDetail } from '@/api/types';
import { isPushActive } from '@/features/push/pushStore';
import { sosKeys } from '@/features/sos/queries';

/** Must stay under ['sos', 'alerts']: push payloads invalidate that prefix (features/push/payload.ts). */
export const alertKeys = {
  all: ['sos', 'alerts'] as const,
  feed: () => ['sos', 'alerts', 'feed'] as const,
  open: () => ['sos', 'alerts', 'open'] as const,
};

/** Without push, a new nearby SOS is noticed by polling only while the app is open. */
const OPEN_ALERTS_POLL_MS = 30_000;
const OPEN_ALERTS_POLL_WITH_PUSH_MS = 120_000;

export function useAlertsFeed() {
  return useInfiniteQuery({
    queryKey: alertKeys.feed(),
    queryFn: ({ pageParam }) => sosApi.alerts({ page: pageParam, pageSize: 20 }),
    initialPageParam: 1,
    getNextPageParam: (last) => (last.page < last.totalPages ? last.page + 1 : undefined),
  });
}

/** Nearby SOS that still need help (for the home card). */
export function useOpenAlerts() {
  return useQuery({
    queryKey: alertKeys.open(),
    queryFn: async () => (await sosApi.alerts({ openOnly: true, page: 1, pageSize: 10 })).items,
    refetchInterval: () => (isPushActive() ? OPEN_ALERTS_POLL_WITH_PUSH_MS : OPEN_ALERTS_POLL_MS),
  });
}

/** "I've seen it": sent once when an alerted neighbour opens the SOS. */
export function useAcknowledgeAlert() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (sosId: string) => sosApi.acknowledge(sosId),
    onSuccess: () => void client.invalidateQueries({ queryKey: alertKeys.all }),
  });
}

export function useRespondToAlert() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ sosId, response, note }: { sosId: string; response: Exclude<ResponseType, 'None'>; note?: string }) =>
      sosApi.respond(sosId, response, note),
    onSuccess: (sos: SosDetail) => {
      client.setQueryData(sosKeys.detail(sos.id), sos);
      void client.invalidateQueries({ queryKey: alertKeys.all });
    },
  });
}
