import { useTranslation } from 'react-i18next';
import { Alert, Linking, Pressable, StyleSheet, Text } from 'react-native';

import { colors, radius, spacing, touchTarget } from './theme';

/** National emergency number. Shown even when signed out or offline: the app must never stand between someone and 112. */
export const PRIMARY_EMERGENCY_NUMBER = '112';

export async function callNumber(number: string, cannotCallMessage: string): Promise<void> {
  try {
    await Linking.openURL(`tel:${number}`);
  } catch {
    Alert.alert(number, cannotCallMessage);
  }
}

export function EmergencyCallBar({ number = PRIMARY_EMERGENCY_NUMBER }: { number?: string }) {
  const { t } = useTranslation();
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={t('emergency.callLabel', { label: t('emergency.title'), number })}
      onPress={() => void callNumber(number, t('emergency.callFailed', { number }))}
      style={({ pressed }) => [styles.bar, pressed && styles.pressed]}
    >
      <Text style={styles.text}>{t('emergency.banner', { number })}</Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  bar: {
    minHeight: touchTarget,
    borderRadius: radius.md,
    borderWidth: 1.5,
    borderColor: colors.primary,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: spacing.md,
  },
  pressed: { backgroundColor: colors.dangerSurface },
  text: { color: colors.primary, fontSize: 16, fontWeight: '700' },
});
