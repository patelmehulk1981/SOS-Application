import { keepPreviousData, useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { adminSosApi, adminUsersApi, invitationsApi } from '@/api/endpoints';
import type {
  AdminSosListQuery,
  AdminUserDetail,
  AdminUserQuery,
  CreateInvitationRequest,
  InvitationStatus,
  MapBounds,
  PagedResult,
  SosDetail,
  SosStatus,
} from '@/api/types';
import { isPushActive } from '@/features/push/pushStore';
import { sosKeys } from '@/features/sos/queries';

export const adminKeys = {
  all: ['admin'] as const,
  sos: () => ['admin', 'sos'] as const,
  sosList: (query: AdminSosListQuery) => ['admin', 'sos', 'list', query] as const,
  queueCount: () => ['admin', 'sos', 'queueCount'] as const,
  map: (bounds: MapBounds, status?: SosStatus, from?: string) => ['admin', 'sos', 'map', bounds, status, from] as const,
  dashboard: (from?: string, adminId?: string) => ['admin', 'sos', 'dashboard', from, adminId] as const,
  invitations: () => ['admin', 'invitations'] as const,
  invitationList: (status?: InvitationStatus) => ['admin', 'invitations', status ?? 'all'] as const,
  users: () => ['admin', 'users'] as const,
  userList: (query: AdminUserQuery) => ['admin', 'users', 'list', query] as const,
  user: (id: string) => ['admin', 'users', 'detail', id] as const,
  referrals: (adminId?: string) => ['admin', 'referrals', adminId ?? 'mine'] as const,
  referralTree: () => ['admin', 'referrals', 'tree'] as const,
};

/**
 * The review queue is polled while it is on screen; with push active (sos.review_requested / sos.escalated
 * refresh it instantly) polling is only a safety net.
 */
const queuePollMs = () => (isPushActive() ? 60_000 : 15_000);
/** The home-screen badge polls more gently. */
const queueCountPollMs = () => (isPushActive() ? 120_000 : 30_000);
const PAGE_SIZE = 20;

const nextPage = <T>(last: PagedResult<T>) => (last.page < last.totalPages ? last.page + 1 : undefined);

// ---- SOS ----------------------------------------------------------------------------------------

/** Pending + Escalated, oldest first (docs/03e). Sub-Admins see only SOS assigned to them. */
export function useReviewQueue() {
  const query: AdminSosListQuery = { awaitingReview: true };
  return useInfiniteQuery({
    queryKey: adminKeys.sosList(query),
    queryFn: ({ pageParam }) => adminSosApi.list({ ...query, page: pageParam, pageSize: PAGE_SIZE }),
    initialPageParam: 1,
    getNextPageParam: nextPage,
    refetchInterval: queuePollMs,
  });
}

/** Number of SOS awaiting this admin's review, plus how many of them are escalated (first page only). */
export function useReviewQueueCount(enabled: boolean) {
  return useQuery({
    queryKey: adminKeys.queueCount(),
    queryFn: async () => {
      const page = await adminSosApi.list({ awaitingReview: true, page: 1, pageSize: 50 });
      return { total: page.totalCount, escalated: page.items.filter((item) => item.status === 'Escalated').length };
    },
    enabled,
    refetchInterval: enabled ? queueCountPollMs : false,
  });
}

export function useAdminSosList(filter: { status?: SosStatus; assignedAdminId?: string }) {
  const query: AdminSosListQuery = { status: filter.status, assignedAdminId: filter.assignedAdminId };
  return useInfiniteQuery({
    queryKey: adminKeys.sosList(query),
    queryFn: ({ pageParam }) => adminSosApi.list({ ...query, page: pageParam, pageSize: PAGE_SIZE }),
    initialPageParam: 1,
    getNextPageParam: nextPage,
  });
}

export function useSosMap(bounds: MapBounds | null, status?: SosStatus, from?: string) {
  return useQuery({
    queryKey: bounds ? adminKeys.map(bounds, status, from) : ['admin', 'sos', 'map', 'none'],
    queryFn: ({ signal }) => adminSosApi.map(bounds!, { status, from }, signal),
    enabled: bounds !== null,
    // Keep the old pins on screen while the new area loads.
    placeholderData: keepPreviousData,
    staleTime: 60_000,
  });
}

export function useDashboard(from?: string, adminId?: string) {
  return useQuery({
    queryKey: adminKeys.dashboard(from, adminId),
    queryFn: () => adminSosApi.dashboard({ from, adminId }),
    placeholderData: keepPreviousData,
  });
}

/** Approve / reject. The result replaces the cached detail; queue, lists and stats are refreshed. */
function useReviewMutation<TArgs>(run: (args: TArgs) => Promise<SosDetail>) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: (sos) => {
      client.setQueryData(sosKeys.detail(sos.id), sos);
      void client.invalidateQueries({ queryKey: adminKeys.sos() });
    },
  });
}

export const useApproveSos = () =>
  useReviewMutation(({ id, remarks }: { id: string; remarks?: string }) => adminSosApi.approve(id, remarks));

export const useRejectSos = () =>
  useReviewMutation(({ id, remarks }: { id: string; remarks: string }) => adminSosApi.reject(id, remarks));

// ---- Invitations --------------------------------------------------------------------------------

export function useInvitations(status?: InvitationStatus) {
  return useInfiniteQuery({
    queryKey: adminKeys.invitationList(status),
    queryFn: ({ pageParam }) => invitationsApi.list({ status, page: pageParam, pageSize: PAGE_SIZE }),
    initialPageParam: 1,
    getNextPageParam: nextPage,
  });
}

export function useCreateInvitation() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (request: CreateInvitationRequest) => invitationsApi.create(request),
    onSuccess: () => void client.invalidateQueries({ queryKey: adminKeys.invitations() }),
  });
}

export function useRevokeInvitation() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => invitationsApi.revoke(id),
    onSuccess: () => void client.invalidateQueries({ queryKey: adminKeys.invitations() }),
  });
}

// ---- Users and referrals ------------------------------------------------------------------------

export function useAdminUsers(filter: Omit<AdminUserQuery, 'page' | 'pageSize'>) {
  return useInfiniteQuery({
    queryKey: adminKeys.userList(filter),
    queryFn: ({ pageParam }) => adminUsersApi.list({ ...filter, page: pageParam, pageSize: PAGE_SIZE }),
    initialPageParam: 1,
    getNextPageParam: nextPage,
    placeholderData: keepPreviousData,
  });
}

export function useAdminUser(id: string) {
  return useQuery({ queryKey: adminKeys.user(id), queryFn: () => adminUsersApi.get(id) });
}

export function useSetUserStatus() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, status, reason }: { id: string; status: 'Active' | 'Suspended'; reason?: string }) =>
      adminUsersApi.setStatus(id, status, reason),
    onSuccess: (detail: AdminUserDetail) => {
      client.setQueryData(adminKeys.user(detail.user.id), detail);
      // Suspension also revokes invitations and reroutes SOS: refresh everything admin-side.
      void client.invalidateQueries({ queryKey: adminKeys.all });
    },
  });
}

export function useReferrals(adminId?: string) {
  return useInfiniteQuery({
    queryKey: adminKeys.referrals(adminId),
    queryFn: ({ pageParam }) => adminUsersApi.referrals({ adminId, page: pageParam, pageSize: PAGE_SIZE }),
    initialPageParam: 1,
    getNextPageParam: nextPage,
  });
}

export function useReferralTree(enabled: boolean) {
  return useQuery({ queryKey: adminKeys.referralTree(), queryFn: () => adminUsersApi.referralTree(), enabled });
}
