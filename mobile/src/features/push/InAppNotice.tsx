import { useRouter } from 'expo-router';
import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';

import { colors, radius, spacing, typography } from '@/ui/theme';

import { routeFor } from './payload';
import { usePushStore } from './pushStore';

/** Status updates disappear on their own; emergencies wait for the user. */
const AUTO_DISMISS_MS = 6_000;

/**
 * A push that arrives while the app is open (the OS does not show those). Shown at the top of every
 * signed-in screen; "View" opens the SOS.
 */
export function InAppNotice() {
  const { t } = useTranslation();
  const router = useRouter();
  const insets = useSafeAreaInsets();
  const notice = usePushStore((s) => s.notice);

  useEffect(() => {
    if (!notice || notice.urgent) return;
    const timer = setTimeout(() => {
      if (usePushStore.getState().notice?.id === notice.id) usePushStore.setState({ notice: null });
    }, AUTO_DISMISS_MS);
    return () => clearTimeout(timer);
  }, [notice]);

  if (!notice) return null;

  const dismiss = () => usePushStore.setState({ notice: null });
  const open = () => {
    dismiss();
    router.push(routeFor(notice.payload));
  };

  return (
    <View style={[styles.wrap, { top: insets.top + spacing.sm }]} pointerEvents="box-none">
      <View
        style={[styles.card, notice.urgent && styles.urgent]}
        accessibilityRole="alert"
        accessibilityLiveRegion={notice.urgent ? 'assertive' : 'polite'}
      >
        <Pressable style={styles.text} onPress={open} accessibilityRole="button" accessibilityHint={t('push.noticeView')}>
          <Text style={[typography.label, notice.urgent && styles.urgentText]} numberOfLines={2}>
            {notice.title}
          </Text>
          {notice.body ? (
            <Text style={[typography.small, notice.urgent && styles.urgentText]} numberOfLines={3}>
              {notice.body}
            </Text>
          ) : null}
        </Pressable>
        <View style={styles.actions}>
          <Pressable onPress={open} style={styles.action} accessibilityRole="button">
            <Text style={[styles.actionText, notice.urgent && styles.urgentText]}>{t('push.noticeView')}</Text>
          </Pressable>
          <Pressable onPress={dismiss} style={styles.action} accessibilityRole="button" accessibilityLabel={t('push.noticeDismiss')} hitSlop={8}>
            <Text style={[styles.close, notice.urgent && styles.urgentText]}>✕</Text>
          </Pressable>
        </View>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { position: 'absolute', left: spacing.md, right: spacing.md, zIndex: 100 },
  card: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    backgroundColor: colors.background,
    borderRadius: radius.lg,
    padding: spacing.md,
    borderWidth: 1,
    borderColor: colors.border,
    elevation: 8,
    shadowColor: '#000',
    shadowOpacity: 0.2,
    shadowRadius: 10,
    shadowOffset: { width: 0, height: 4 },
  },
  urgent: { backgroundColor: colors.primary, borderColor: colors.primary },
  urgentText: { color: colors.onPrimary },
  text: { flex: 1, gap: 2 },
  actions: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs },
  action: { minHeight: 44, minWidth: 44, alignItems: 'center', justifyContent: 'center', paddingHorizontal: spacing.xs },
  actionText: { fontSize: 16, fontWeight: '700', color: colors.primary },
  close: { fontSize: 18, color: colors.textMuted },
});
