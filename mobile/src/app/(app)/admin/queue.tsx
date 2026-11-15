import { useTranslation } from 'react-i18next';
import { Text } from 'react-native';

import { useSessionStore } from '@/auth/sessionStore';
import { AdminSosRow } from '@/features/admin/AdminSosRow';
import { useReviewQueue } from '@/features/admin/queries';
import { PagedList } from '@/ui/PagedList';
import { typography } from '@/ui/theme';

/** Pending + Escalated SOS, oldest first, refreshed every 15 s while open. */
export default function ReviewQueueScreen() {
  const { t } = useTranslation();
  const isMaster = useSessionStore((s) => s.user?.role === 'MasterAdmin');
  const query = useReviewQueue();

  return (
    <PagedList
      query={query}
      keyExtractor={(item) => item.id}
      renderItem={(item) => <AdminSosRow item={item} waiting showAssignee={isMaster} />}
      emptyText={t('admin.queue.empty')}
      header={<Text style={typography.small}>{t('admin.queue.intro')}</Text>}
    />
  );
}
