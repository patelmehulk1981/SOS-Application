import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Linking, StyleSheet, Text, View } from 'react-native';

import { Button } from '@/ui/Button';
import { colors, radius, spacing, typography } from '@/ui/theme';

import { requestPushPermission } from './push';
import { usePushStore } from './pushStore';

/**
 * Asks for notification permission in context (with the reason) instead of a cold system prompt at launch.
 * Hidden when granted, or when this build has no push (development without Firebase).
 */
export function PushPrompt({ admin }: { admin: boolean }) {
  const { t } = useTranslation();
  const permission = usePushStore((s) => s.permission);
  const [busy, setBusy] = useState(false);

  if (permission === 'granted' || permission === 'unavailable') return null;
  const denied = permission === 'denied';

  const onPress = async () => {
    if (denied) {
      await Linking.openSettings();
      return;
    }
    setBusy(true);
    try {
      await requestPushPermission();
    } finally {
      setBusy(false);
    }
  };

  return (
    <View style={styles.card}>
      <Text style={typography.heading}>{t('push.promptTitle')}</Text>
      <Text style={typography.body}>
        {denied ? t('push.promptDeniedBody') : admin ? t('push.promptBodyAdmin') : t('push.promptBody')}
      </Text>
      <Button
        title={denied ? t('push.promptOpenSettings') : t('push.promptAllow')}
        onPress={() => void onPress()}
        loading={busy}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  card: { backgroundColor: colors.infoSurface, borderRadius: radius.lg, padding: spacing.md, gap: spacing.sm },
});
