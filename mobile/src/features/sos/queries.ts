import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { sosApi } from '@/api/endpoints';
import { OPEN_SOS_STATUSES, type SosDetail, type SosStatus } from '@/api/types';
import { isPushActive } from '@/features/push/pushStore';

export const sosKeys = {
  all: ['sos'] as const,
  mine: () => ['sos', 'mine'] as const,
  active: () => ['sos', 'mine', 'active'] as const,
  detail: (id: string) => ['sos', 'detail', id] as const,
};

export const isOpenStatus = (status: SosStatus) => OPEN_SOS_STATUSES.includes(status);

/**
 * While an SOS is open its status changes within minutes (review, escalation, responders). Push reports
 * those changes when it is active; polling stays as a slower safety net (a push can be delayed or dropped).
 */
const OPEN_POLL_MS = 15_000;
const OPEN_POLL_WITH_PUSH_MS = 60_000;
const openPollMs = () => (isPushActive() ? OPEN_POLL_WITH_PUSH_MS : OPEN_POLL_MS);
/** Photo URLs are signed for 5 minutes: refresh a little before that while the screen is open. */
const PHOTO_URL_REFRESH_MS = 4 * 60_000;

export function useSosDetail(id: string) {
  return useQuery({
    queryKey: sosKeys.detail(id),
    queryFn: () => sosApi.get(id),
    refetchInterval: (query) => {
      const data = query.state.data;
      if (!data) return false;
      return isOpenStatus(data.status) ? openPollMs() : data.photos.length > 0 ? PHOTO_URL_REFRESH_MS : false;
    },
  });
}

/** The caller's open SOS, if any (there is at most one). */
export function useActiveSos() {
  return useQuery({
    queryKey: sosKeys.active(),
    queryFn: async () => {
      const page = await sosApi.mine({ page: 1, pageSize: 5 });
      return page.items.find((item) => isOpenStatus(item.status)) ?? null;
    },
    refetchInterval: (query) => (query.state.data ? openPollMs() : false),
  });
}

export function useMySosList() {
  return useInfiniteQuery({
    queryKey: sosKeys.mine(),
    queryFn: ({ pageParam }) => sosApi.mine({ page: pageParam, pageSize: 20 }),
    initialPageParam: 1,
    getNextPageParam: (last) => (last.page < last.totalPages ? last.page + 1 : undefined),
  });
}

function useSosMutation<TArgs>(run: (args: TArgs) => Promise<SosDetail>) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: (sos) => {
      client.setQueryData(sosKeys.detail(sos.id), sos);
      void client.invalidateQueries({ queryKey: sosKeys.mine() });
    },
  });
}

export const useCancelSos = () =>
  useSosMutation(({ id, reason }: { id: string; reason?: string }) => sosApi.cancel(id, reason));

export const useResolveSos = () =>
  useSosMutation(({ id, remarks }: { id: string; remarks?: string }) => sosApi.resolve(id, remarks));
