import { StyleSheet, Text, View } from 'react-native';

import { colors, radius, spacing, typography } from './theme';

type Tone = 'error' | 'info' | 'success';

const palette: Record<Tone, { background: string; border: string }> = {
  error: { background: colors.dangerSurface, border: colors.danger },
  info: { background: colors.infoSurface, border: colors.info },
  success: { background: colors.successSurface, border: colors.success },
};

export function Banner({ tone, message, detail }: { tone: Tone; message: string; detail?: string }) {
  return (
    <View
      style={[styles.banner, { backgroundColor: palette[tone].background, borderLeftColor: palette[tone].border }]}
      accessibilityRole={tone === 'error' ? 'alert' : 'text'}
      accessibilityLiveRegion={tone === 'error' ? 'assertive' : 'polite'}
    >
      <Text style={typography.body}>{message}</Text>
      {detail ? <Text style={typography.small}>{detail}</Text> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  banner: { borderLeftWidth: 4, borderRadius: radius.sm, padding: spacing.md, gap: spacing.xs },
});
