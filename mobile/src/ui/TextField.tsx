import { useState, type ComponentProps } from 'react';
import { StyleSheet, Text, TextInput, View } from 'react-native';

import { colors, radius, spacing, touchTarget, typography } from './theme';

export interface TextFieldProps extends ComponentProps<typeof TextInput> {
  label: string;
  hint?: string;
  error?: string | null;
  /** Fixed text shown before the input, e.g. "+91". */
  prefix?: string;
}

export function TextField({ label, hint, error, prefix, onFocus, onBlur, style, ref, ...inputProps }: TextFieldProps) {
  const [focused, setFocused] = useState(false);
  const describedBy = error ?? hint;

  return (
    <View style={styles.container}>
      <Text style={typography.label}>{label}</Text>
      <View style={[styles.box, focused && styles.boxFocused, error ? styles.boxError : null]}>
        {prefix ? <Text style={styles.prefix}>{prefix}</Text> : null}
        <TextInput
          ref={ref}
          accessibilityLabel={label}
          accessibilityHint={describedBy ?? undefined}
          placeholderTextColor={colors.disabled}
          style={[styles.input, style]}
          onFocus={(event) => {
            setFocused(true);
            onFocus?.(event);
          }}
          onBlur={(event) => {
            setFocused(false);
            onBlur?.(event);
          }}
          {...inputProps}
        />
      </View>
      {error ? (
        <Text style={styles.error} accessibilityLiveRegion="polite">
          {error}
        </Text>
      ) : hint ? (
        <Text style={typography.small}>{hint}</Text>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  container: { gap: spacing.xs },
  box: {
    minHeight: touchTarget + 4,
    flexDirection: 'row',
    alignItems: 'center',
    borderWidth: 1.5,
    borderColor: colors.border,
    borderRadius: radius.md,
    paddingHorizontal: spacing.md,
    backgroundColor: colors.background,
  },
  boxFocused: { borderColor: colors.borderFocused },
  boxError: { borderColor: colors.danger },
  prefix: { ...typography.body, fontSize: 18, marginRight: spacing.sm, color: colors.textMuted },
  input: { flex: 1, fontSize: 18, color: colors.text, paddingVertical: spacing.sm },
  error: { fontSize: 14, color: colors.danger },
});
