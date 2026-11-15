import { Stack, useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';

import type { SosStatus } from '@/api/types';
import { useSessionStore } from '@/auth/sessionStore';
import { AdminSosRow } from '@/features/admin/AdminSosRow';
import { useAdminSosList } from '@/features/admin/queries';
import { Chips } from '@/ui/Chips';
import { PagedList } from '@/ui/PagedList';

type Filter = SosStatus | 'all';
const FILTERS: readonly Filter[] = ['all', 'Pending', 'Escalated', 'Approved', 'Resolved', 'Rejected', 'Cancelled', 'Expired'];

/**
 * SOS history, newest first. The Master Admin can open it for one Sub-Admin
 * (`assignedAdminId` + `name`, from the member screen).
 */
export default function AdminSosListScreen() {
  const { t } = useTranslation();
  const params = useLocalSearchParams<{ assignedAdminId?: string; name?: string }>();
  const isMaster = useSessionStore((s) => s.user?.role === 'MasterAdmin');
  const [filter, setFilter] = useState<Filter>('all');
  const query = useAdminSosList({
    status: filter === 'all' ? undefined : filter,
    assignedAdminId: isMaster ? params.assignedAdminId : undefined,
  });

  return (
    <>
      {params.name ? <Stack.Screen options={{ title: t('admin.sosList.titleFor', { name: params.name }) }} /> : null}
      <PagedList
        query={query}
        keyExtractor={(item) => item.id}
        renderItem={(item) => <AdminSosRow item={item} showAssignee={isMaster && !params.assignedAdminId} />}
        emptyText={t('admin.sosList.empty')}
        header={
          <Chips
            options={FILTERS.map((f) => ({ value: f, label: f === 'all' ? t('admin.filterAll') : t(`sos.status.${f}`) }))}
            value={filter}
            onChange={setFilter}
          />
        }
      />
    </>
  );
}
