import type { ReactNode } from 'react';
import { Pressable, StyleSheet, Switch, Text, View } from 'react-native';

import { colors, radius, spacing, touchTarget, typography } from './theme';

export function Section({ title, children }: { title?: string; children: ReactNode }) {
  return (
    <View style={styles.section}>
      {title ? (
        <Text style={styles.sectionTitle} accessibilityRole="header">
          {title}
        </Text>
      ) : null}
      <View style={styles.card}>{children}</View>
    </View>
  );
}

export function InfoRow({ label, value }: { label?: string; value: string }) {
  return (
    <View style={styles.row} accessible accessibilityLabel={label ? `${label}: ${value}` : value}>
      {label ? <Text style={typography.small}>{label}</Text> : null}
      <Text style={typography.body}>{value}</Text>
    </View>
  );
}

export function LinkRow({ title, onPress, destructive = false }: { title: string; onPress: () => void; destructive?: boolean }) {
  return (
    <Pressable
      accessibilityRole="button"
      onPress={onPress}
      style={({ pressed }) => [styles.row, styles.linkRow, pressed && styles.pressed]}
    >
      <Text style={[typography.body, destructive ? styles.destructive : styles.link]}>{title}</Text>
      <Text style={styles.chevron} accessible={false}>
        ›
      </Text>
    </Pressable>
  );
}

export function SwitchRow({
  title,
  help,
  value,
  onChange,
  disabled = false,
}: {
  title: string;
  help?: string;
  value: boolean;
  onChange: (value: boolean) => void;
  disabled?: boolean;
}) {
  return (
    <View style={[styles.row, styles.switchRow]}>
      <View style={styles.switchText}>
        <Text style={typography.body}>{title}</Text>
        {help ? <Text style={typography.small}>{help}</Text> : null}
      </View>
      <Switch
        accessibilityLabel={title}
        accessibilityHint={help}
        value={value}
        onValueChange={onChange}
        disabled={disabled}
        trackColor={{ true: colors.primary, false: colors.border }}
        thumbColor={colors.background}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  section: { gap: spacing.sm },
  sectionTitle: { ...typography.label, color: colors.textMuted, textTransform: 'uppercase', fontSize: 13 },
  card: { backgroundColor: colors.surface, borderRadius: radius.lg, overflow: 'hidden' },
  row: { minHeight: touchTarget + 8, paddingHorizontal: spacing.md, paddingVertical: spacing.sm, justifyContent: 'center', gap: 2 },
  linkRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  switchRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  switchText: { flex: 1, gap: 2 },
  pressed: { backgroundColor: colors.border },
  link: { color: colors.info, fontWeight: '600' },
  destructive: { color: colors.danger, fontWeight: '600' },
  chevron: { fontSize: 22, color: colors.textMuted },
});
