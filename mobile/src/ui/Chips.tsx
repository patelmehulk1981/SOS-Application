import { Pressable, ScrollView, StyleSheet, Text } from 'react-native';

import { colors, radius, spacing } from './theme';

export interface ChipOption<T extends string> {
  value: T;
  label: string;
}

/** Single-select filter pills in a horizontally scrolling row. */
export function Chips<T extends string>({
  options,
  value,
  onChange,
  accessibilityLabel,
}: {
  options: readonly ChipOption<T>[];
  value: T;
  onChange: (value: T) => void;
  accessibilityLabel?: string;
}) {
  return (
    <ScrollView
      horizontal
      showsHorizontalScrollIndicator={false}
      contentContainerStyle={styles.row}
      accessibilityRole="radiogroup"
      accessibilityLabel={accessibilityLabel}
    >
      {options.map((option) => {
        const selected = option.value === value;
        return (
          <Pressable
            key={option.value}
            accessibilityRole="radio"
            accessibilityState={{ selected, checked: selected }}
            onPress={() => onChange(option.value)}
            hitSlop={{ top: 6, bottom: 6 }}
            style={({ pressed }) => [styles.chip, selected && styles.selected, pressed && !selected && styles.pressed]}
          >
            <Text style={[styles.text, selected && styles.selectedText]}>{option.label}</Text>
          </Pressable>
        );
      })}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  row: { gap: spacing.sm, paddingVertical: spacing.xs },
  chip: {
    minHeight: 40,
    justifyContent: 'center',
    paddingHorizontal: spacing.md,
    borderRadius: radius.pill,
    borderWidth: 1.5,
    borderColor: colors.border,
    backgroundColor: colors.background,
  },
  selected: { backgroundColor: colors.text, borderColor: colors.text },
  pressed: { backgroundColor: colors.surface },
  text: { fontSize: 15, fontWeight: '600', color: colors.text },
  selectedText: { color: colors.background },
});
