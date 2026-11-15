import { useRouter } from 'expo-router';
import { useTranslation } from 'react-i18next';
import { Pressable, StyleSheet, Text, View } from 'react-native';

import type { AdminSosListItem } from '@/api/types';
import { StatusBadge } from '@/features/sos/StatusBadge';
import { formatDateTime } from '@/lib/format';
import { formatPhone } from '@/lib/phone';
import { Pill } from '@/ui/Pill';
import { colors, radius, spacing, typography } from '@/ui/theme';

import { minutesSince } from './logic';

/**
 * One SOS in the review queue or the admin history. `waiting` shows how long it has been waiting
 * (queue) instead of the creation time. Opens the shared detail screen, which shows review actions.
 */
export function AdminSosRow({ item, waiting = false, showAssignee = false }: { item: AdminSosListItem; waiting?: boolean; showAssignee?: boolean }) {
  const { t } = useTranslation();
  const router = useRouter();
  const escalated = item.status === 'Escalated';
  const name = item.requester.fullName ?? t('admin.users.noName');

  return (
    <Pressable
      accessibilityRole="button"
      onPress={() => router.push({ pathname: '/sos/[id]', params: { id: item.id } })}
      style={({ pressed }) => [styles.row, escalated && styles.escalated, pressed && styles.pressed]}
    >
      <View style={styles.top}>
        <Text style={typography.heading}>{t(`sos.category.${item.category}`)}</Text>
        <Text style={typography.small}>{item.referenceNo}</Text>
      </View>
      <View style={styles.badges}>
        <StatusBadge status={item.status} />
        {item.isAutoApproved ? <Pill label={t('admin.sosRow.autoApproved')} tone="info" /> : null}
      </View>
      <Text style={[typography.label, waiting && escalated && styles.urgent]}>
        {waiting ? <WaitingText createdAt={item.createdAt} /> : formatDateTime(item.createdAt)}
      </Text>
      <Text style={typography.body}>
        {name}
        {item.requesterPhone ? ` · ${formatPhone(item.requesterPhone)}` : ''}
      </Text>
      {item.address ? (
        <Text style={typography.small} numberOfLines={2}>
          {item.address}
        </Text>
      ) : null}
      <Text style={typography.small}>
        {[
          item.photoCount > 0 ? t('sos.history.photos', { count: item.photoCount }) : null,
          item.recipientCount > 0 ? t('admin.sosRow.alerted', { count: item.recipientCount }) : null,
          showAssignee ? t('admin.sosRow.assignedTo', { name: item.assignedAdmin.fullName ?? t(`common.roles.${item.assignedAdmin.role}`) }) : null,
        ]
          .filter(Boolean)
          .join(' · ')}
      </Text>
    </Pressable>
  );
}

function WaitingText({ createdAt }: { createdAt: string }) {
  const { t } = useTranslation();
  // Re-rendered by the queue's 15 s poll, which is precise enough for minutes.
  const minutes = minutesSince(createdAt);
  if (minutes < 1) return t('admin.queue.waitingNow');
  if (minutes < 60) return t('admin.queue.waitingMinutes', { count: minutes });
  return t('admin.queue.waitingHours', { hours: Math.floor(minutes / 60), minutes: minutes % 60 });
}

const styles = StyleSheet.create({
  row: { backgroundColor: colors.surface, borderRadius: radius.lg, padding: spacing.md, gap: spacing.xs, borderWidth: 2, borderColor: 'transparent' },
  escalated: { borderColor: colors.primary },
  pressed: { backgroundColor: colors.border },
  top: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  badges: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm, alignItems: 'center' },
  urgent: { color: colors.primary },
});
