import { ActivityIndicator, Pressable, StyleSheet, Text, type PressableProps } from 'react-native';

import { colors, radius, spacing, touchTarget } from './theme';

type Variant = 'primary' | 'secondary' | 'ghost';

export interface ButtonProps extends Omit<PressableProps, 'children' | 'style'> {
  title: string;
  variant?: Variant;
  loading?: boolean;
}

export function Button({ title, variant = 'primary', loading = false, disabled, ...rest }: ButtonProps) {
  const inactive = Boolean(disabled) || loading;
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled: inactive, busy: loading }}
      disabled={inactive}
      style={({ pressed }) => [
        styles.base,
        styles[variant],
        pressed && !inactive && (variant === 'primary' ? styles.primaryPressed : styles.pressed),
        inactive && variant === 'primary' && styles.primaryDisabled,
      ]}
      {...rest}
    >
      {loading ? (
        <ActivityIndicator color={variant === 'primary' ? colors.onPrimary : colors.primary} />
      ) : (
        <Text style={[styles.text, variant === 'primary' ? styles.textPrimary : styles.textOther, inactive && variant !== 'primary' && styles.textDisabled]}>
          {title}
        </Text>
      )}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  base: {
    minHeight: touchTarget + 4,
    borderRadius: radius.md,
    paddingHorizontal: spacing.lg,
    alignItems: 'center',
    justifyContent: 'center',
  },
  primary: { backgroundColor: colors.primary },
  primaryPressed: { backgroundColor: colors.primaryPressed },
  primaryDisabled: { backgroundColor: colors.disabled },
  secondary: { backgroundColor: colors.background, borderWidth: 1.5, borderColor: colors.primary },
  ghost: { backgroundColor: 'transparent' },
  pressed: { backgroundColor: colors.surface },
  text: { fontSize: 17, fontWeight: '600' },
  textPrimary: { color: colors.onPrimary },
  textOther: { color: colors.primary },
  textDisabled: { color: colors.disabled },
});
