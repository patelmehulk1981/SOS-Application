import { useTranslation } from 'react-i18next';
import { StyleSheet, Text, View } from 'react-native';

import type { SosStatus } from '@/api/types';
import { colors, radius, spacing } from '@/ui/theme';

const tones: Record<SosStatus, { background: string; text: string }> = {
  Pending: { background: '#FFF4E5', text: '#8A4B00' },
  Escalated: { background: '#FFE9D6', text: '#8A3200' },
  Approved: { background: colors.dangerSurface, text: colors.danger },
  Rejected: { background: colors.surface, text: colors.textMuted },
  Cancelled: { background: colors.surface, text: colors.textMuted },
  Resolved: { background: colors.successSurface, text: colors.success },
  Expired: { background: colors.surface, text: colors.textMuted },
};

export function StatusBadge({ status }: { status: SosStatus }) {
  const { t } = useTranslation();
  const tone = tones[status];
  return (
    <View style={[styles.badge, { backgroundColor: tone.background }]}>
      <Text style={[styles.text, { color: tone.text }]}>{t(`sos.status.${status}`)}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  badge: { alignSelf: 'flex-start', borderRadius: radius.pill, paddingHorizontal: spacing.md, paddingVertical: spacing.xs },
  text: { fontSize: 14, fontWeight: '700' },
});
