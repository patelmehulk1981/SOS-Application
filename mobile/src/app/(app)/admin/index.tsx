import { useRouter } from 'expo-router';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ActivityIndicator, RefreshControl, ScrollView, StyleSheet, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import type { DashboardStats } from '@/api/types';
import { useSessionStore } from '@/auth/sessionStore';
import { PERIODS, periodStart, splitDuration, type Period } from '@/features/admin/logic';
import { useDashboard, useReviewQueueCount } from '@/features/admin/queries';
import { Banner } from '@/ui/Banner';
import { Button } from '@/ui/Button';
import { Chips } from '@/ui/Chips';
import { describeError } from '@/ui/describeError';
import { LinkRow, Section } from '@/ui/Rows';
import { colors, radius, spacing, typography } from '@/ui/theme';

export default function AdminHubScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const isMaster = useSessionStore((s) => s.user?.role === 'MasterAdmin');
  const [period, setPeriod] = useState<Period>('7d');
  // Recomputed only when the period changes, so the query key is stable between renders.
  const from = useMemo(() => periodStart(period), [period]);
  const stats = useDashboard(from);
  const queue = useReviewQueueCount(true);
  const waiting = queue.data?.total ?? 0;

  return (
    <SafeAreaView style={styles.safe} edges={['bottom', 'left', 'right']}>
      <ScrollView
        contentContainerStyle={styles.content}
        contentInsetAdjustmentBehavior="automatic"
        refreshControl={
          <RefreshControl
            refreshing={stats.isRefetching && !stats.isPlaceholderData}
            onRefresh={() => {
              void stats.refetch();
              void queue.refetch();
            }}
          />
        }
      >
        <Text style={typography.small}>{isMaster ? t('admin.hub.scopeMaster') : t('admin.hub.scopeSub')}</Text>

        <Section>
          <LinkRow
            title={waiting > 0 ? t('admin.hub.links.queueCount', { count: waiting }) : t('admin.hub.links.queue')}
            onPress={() => router.push('/admin/queue')}
          />
          <LinkRow title={t('admin.hub.links.sos')} onPress={() => router.push('/admin/sos')} />
          <LinkRow title={t('admin.hub.links.map')} onPress={() => router.push('/admin/map')} />
          <LinkRow title={t('admin.hub.links.invitations')} onPress={() => router.push('/admin/invitations')} />
          <LinkRow title={t('admin.hub.links.users')} onPress={() => router.push('/admin/users')} />
          <LinkRow title={t('admin.hub.links.referrals')} onPress={() => router.push('/admin/referrals')} />
        </Section>

        <Chips
          options={PERIODS.map((p) => ({ value: p, label: t(`admin.periods.${p}`) }))}
          value={period}
          onChange={setPeriod}
        />

        {stats.data ? (
          <Stats stats={stats.data} isMaster={isMaster} />
        ) : stats.isError ? (
          <View style={styles.gap}>
            <Banner tone="error" message={describeError(stats.error, t).message} />
            <Button variant="secondary" title={t('common.retry')} onPress={() => void stats.refetch()} />
          </View>
        ) : (
          <ActivityIndicator size="large" color={colors.primary} />
        )}
      </ScrollView>
    </SafeAreaView>
  );
}

function Stats({ stats, isMaster }: { stats: DashboardStats; isMaster: boolean }) {
  const { t } = useTranslation();
  const avg = stats.avgReviewSeconds === undefined ? null : splitDuration(stats.avgReviewSeconds);

  return (
    <View style={styles.gap}>
      <Text style={styles.groupTitle} accessibilityRole="header">
        {t('admin.stats.sosTitle')}
      </Text>
      <View style={styles.grid}>
        <Tile label={t('admin.stats.totalSos')} value={stats.totalSos} />
        <Tile label={t('admin.stats.awaiting')} value={stats.pendingSos + stats.escalatedSos} alert={stats.pendingSos + stats.escalatedSos > 0} />
        <Tile label={t('admin.stats.active')} value={stats.activeSos} />
        <Tile label={t('admin.stats.approved')} value={stats.approvedSos} />
        <Tile label={t('admin.stats.rejected')} value={stats.rejectedSos} />
        <Tile label={t('admin.stats.resolved')} value={stats.resolvedSos} />
        <Tile label={t('admin.stats.escalated')} value={stats.escalatedSos} />
        <Tile label={t('admin.stats.autoApproved')} value={stats.autoApprovedSos} />
        <Tile label={t('admin.stats.cancelled')} value={stats.cancelledSos} />
        <Tile label={t('admin.stats.expired')} value={stats.expiredSos} />
      </View>
      <Section>
        <Row
          label={t('admin.stats.avgReview')}
          value={avg ? t('admin.stats.avgReviewValue', { minutes: avg.minutes, seconds: avg.seconds }) : t('admin.stats.avgReviewNone')}
        />
        <Row label={t('admin.stats.last24h')} value={String(stats.sosLast24Hours)} />
      </Section>

      <Text style={styles.groupTitle} accessibilityRole="header">
        {t('admin.stats.peopleTitle')}
      </Text>
      <View style={styles.grid}>
        <Tile label={t('admin.stats.users')} value={stats.totalUsers} />
        {isMaster ? <Tile label={t('admin.stats.subAdmins')} value={stats.subAdmins} /> : null}
        {isMaster ? <Tile label={t('admin.stats.normalUsers')} value={stats.normalUsers} /> : null}
        <Tile label={t('admin.stats.newUsers')} value={stats.newUsersLast7Days} />
      </View>
    </View>
  );
}

function Tile({ label, value, alert = false }: { label: string; value: number; alert?: boolean }) {
  return (
    <View style={[styles.tile, alert && styles.tileAlert]} accessible accessibilityLabel={`${label}: ${value}`}>
      <Text style={[styles.tileValue, alert && styles.tileValueAlert]}>{value.toLocaleString()}</Text>
      <Text style={typography.small}>{label}</Text>
    </View>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <View style={styles.row} accessible accessibilityLabel={`${label}: ${value}`}>
      <Text style={[typography.body, styles.rowLabel]}>{label}</Text>
      <Text style={typography.label}>{value}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.background },
  content: { padding: spacing.lg, gap: spacing.lg },
  gap: { gap: spacing.md },
  groupTitle: { ...typography.label, color: colors.textMuted, textTransform: 'uppercase', fontSize: 13 },
  grid: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  tile: { flexBasis: '47%', flexGrow: 1, backgroundColor: colors.surface, borderRadius: radius.lg, padding: spacing.md, gap: 2 },
  tileAlert: { backgroundColor: colors.dangerSurface },
  tileValue: { fontSize: 28, fontWeight: '700', color: colors.text },
  tileValueAlert: { color: colors.danger },
  row: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: spacing.md, padding: spacing.md },
  rowLabel: { flex: 1 },
});
