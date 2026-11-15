import { useRouter } from 'expo-router';
import { useTranslation } from 'react-i18next';
import { Pressable, StyleSheet, Text, View } from 'react-native';

import type { SosAlertItem } from '@/api/types';
import { useAlertsFeed } from '@/features/alerts/queries';
import { formatDateTime, formatDistance } from '@/lib/format';
import { PagedList } from '@/ui/PagedList';
import { Pill } from '@/ui/Pill';
import { colors, radius, spacing, typography } from '@/ui/theme';

/**
 * SOS I was alerted about, newest first. Open ones (still needing help) can be opened; closed ones are kept
 * as a record only (the API no longer shows their details or address to neighbours).
 */
export default function AlertsScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const query = useAlertsFeed();

  return (
    <PagedList
      query={query}
      keyExtractor={(item) => item.sosId}
      emptyText={t('alerts.empty')}
      header={<Text style={typography.small}>{t('alerts.needsLocation')}</Text>}
      renderItem={(item) => <AlertRow item={item} onOpen={() => router.push({ pathname: '/sos/[id]', params: { id: item.sosId } })} />}
    />
  );
}

function AlertRow({ item, onOpen }: { item: SosAlertItem; onOpen: () => void }) {
  const { t } = useTranslation();
  const open = item.status === 'Approved';

  const content = (
    <>
      <View style={styles.top}>
        <Text style={typography.heading}>{t(`sos.category.${item.category}`)}</Text>
        <Pill label={open ? t('alerts.open') : t('alerts.closed')} tone={open ? 'danger' : 'neutral'} />
      </View>
      <Text style={typography.label}>{t('alerts.away', { distance: formatDistance(item.distanceMeters) })}</Text>
      {item.address ? (
        <Text style={typography.body} numberOfLines={2}>
          {item.address}
        </Text>
      ) : null}
      <Text style={typography.small}>
        {formatDateTime(item.alertedAt)}
        {item.response !== 'None' ? ` · ${t('alerts.yourResponse', { response: t(`response.short.${item.response}`) })}` : ''}
      </Text>
    </>
  );

  return open ? (
    <Pressable accessibilityRole="button" onPress={onOpen} style={({ pressed }) => [styles.row, styles.open, pressed && styles.pressed]}>
      {content}
    </Pressable>
  ) : (
    <View style={styles.row}>{content}</View>
  );
}

const styles = StyleSheet.create({
  row: { backgroundColor: colors.surface, borderRadius: radius.lg, padding: spacing.md, gap: spacing.xs, borderWidth: 2, borderColor: 'transparent' },
  open: { borderColor: colors.primary },
  pressed: { backgroundColor: colors.border },
  top: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', gap: spacing.sm },
});
