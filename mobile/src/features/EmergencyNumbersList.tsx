import { useTranslation } from 'react-i18next';
import { Pressable, StyleSheet, Text, View } from 'react-native';

import { callNumber } from '@/ui/EmergencyCallBar';
import { colors, radius, spacing, touchTarget, typography } from '@/ui/theme';
import { FALLBACK_EMERGENCY_NUMBERS, useAppInfo } from './appInfo';

/** Tap-to-call list from GET /app-info, with a built-in fallback so it works offline. */
export function EmergencyNumbersList() {
  const { t } = useTranslation();
  const appInfo = useAppInfo();
  const numbers = appInfo.data?.emergencyNumbers.length ? appInfo.data.emergencyNumbers : FALLBACK_EMERGENCY_NUMBERS;

  return (
    <View style={styles.card}>
      <Text style={typography.heading} accessibilityRole="header">
        {t('emergency.title')}
      </Text>
      {numbers.map((item) => (
        <Pressable
          key={`${item.label}-${item.number}`}
          accessibilityRole="button"
          accessibilityLabel={t('emergency.callLabel', { label: item.label, number: item.number })}
          onPress={() => void callNumber(item.number, t('emergency.callFailed', { number: item.number }))}
          style={({ pressed }) => [styles.row, pressed && styles.pressed]}
        >
          <Text style={typography.body}>{item.label}</Text>
          <Text style={styles.number}>{item.number}</Text>
        </Pressable>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  card: { backgroundColor: colors.surface, borderRadius: radius.lg, padding: spacing.md, gap: spacing.xs },
  row: {
    minHeight: touchTarget,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingHorizontal: spacing.sm,
    borderRadius: radius.sm,
  },
  pressed: { backgroundColor: colors.dangerSurface },
  number: { fontSize: 20, fontWeight: '700', color: colors.primary },
});
