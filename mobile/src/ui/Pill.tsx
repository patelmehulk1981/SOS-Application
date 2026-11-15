import { StyleSheet, Text, View } from 'react-native';

import { colors, radius, spacing } from './theme';

export type PillTone = 'neutral' | 'danger' | 'success' | 'warning' | 'info';

const tones: Record<PillTone, { background: string; text: string }> = {
  neutral: { background: colors.surface, text: colors.textMuted },
  danger: { background: colors.dangerSurface, text: colors.danger },
  success: { background: colors.successSurface, text: colors.success },
  warning: { background: '#FFF4E5', text: '#8A4B00' },
  info: { background: colors.infoSurface, text: colors.info },
};

/** Small status label (colour is never the only signal: the text says it). */
export function Pill({ label, tone = 'neutral' }: { label: string; tone?: PillTone }) {
  const palette = tones[tone];
  return (
    <View style={[styles.pill, { backgroundColor: palette.background }]}>
      <Text style={[styles.text, { color: palette.text }]}>{label}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  pill: { alignSelf: 'flex-start', borderRadius: radius.pill, paddingHorizontal: spacing.sm + 2, paddingVertical: 2 },
  text: { fontSize: 13, fontWeight: '700' },
});
