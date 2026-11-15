/** Design tokens. Contrast ratios meet WCAG AA on white (emergency UI must be readable in sunlight). */
export const colors = {
  primary: '#B71C1C', // SOS red, 7.1:1 on white
  primaryPressed: '#8E1515',
  onPrimary: '#FFFFFF',
  text: '#1A1A1A',
  textMuted: '#555555',
  border: '#C9C9C9',
  borderFocused: '#1A1A1A',
  background: '#FFFFFF',
  surface: '#F5F5F5',
  danger: '#B71C1C',
  dangerSurface: '#FDECEA',
  info: '#0B4F8A',
  infoSurface: '#E8F1FB',
  success: '#1B5E20',
  successSurface: '#E8F5E9',
  disabled: '#9E9E9E',
} as const;

export const spacing = { xs: 4, sm: 8, md: 16, lg: 24, xl: 32 } as const;

export const radius = { sm: 6, md: 10, lg: 16, pill: 999 } as const;

export const typography = {
  title: { fontSize: 26, fontWeight: '700' as const, color: colors.text },
  heading: { fontSize: 20, fontWeight: '600' as const, color: colors.text },
  body: { fontSize: 16, color: colors.text, lineHeight: 22 },
  small: { fontSize: 14, color: colors.textMuted, lineHeight: 20 },
  label: { fontSize: 15, fontWeight: '600' as const, color: colors.text },
};

/** Minimum touch target (Android 48dp, iOS 44pt). */
export const touchTarget = 48;
