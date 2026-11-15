import AsyncStorage from '@react-native-async-storage/async-storage';
import { useRouter } from 'expo-router';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { StyleSheet, Text, View } from 'react-native';

import { useProfile } from '@/features/profile/queries';
import { usePushStore } from '@/features/push/pushStore';
import { Button } from '@/ui/Button';
import { colors, radius, spacing, typography } from '@/ui/theme';

import { useBackgroundLocationStore } from './controller';

const DISMISSED_KEY = 'location.background.promptDismissed';

/**
 * Home card offering background alerts, shown once the basics are in place (alerts and location sharing
 * on, notifications allowed) so prompts never stack. Opens the disclosure screen; never asks the OS directly.
 * "Not now" hides it on this phone; Profile and privacy still offers it.
 */
export function BackgroundPrompt() {
  const { t } = useTranslation();
  const router = useRouter();
  const profile = useProfile();
  const status = useBackgroundLocationStore((s) => s.status);
  const push = usePushStore((s) => s.permission);
  const [dismissed, setDismissed] = useState(true);

  useEffect(() => {
    AsyncStorage.getItem(DISMISSED_KEY)
      .then((value) => setDismissed(value === '1'))
      .catch(() => setDismissed(false));
  }, []);

  const me = profile.data;
  const ready = me?.alertsEnabled === true && me.consents.locationSharing && (push === 'granted' || push === 'unavailable');
  if (dismissed || !ready || status !== 'off') return null;

  const dismiss = () => {
    setDismissed(true);
    void AsyncStorage.setItem(DISMISSED_KEY, '1').catch(() => undefined);
  };

  return (
    <View style={styles.card}>
      <Text style={typography.heading}>{t('home.backgroundTitle')}</Text>
      <Text style={typography.body}>{t('home.backgroundBody')}</Text>
      <Button title={t('home.backgroundOpen')} onPress={() => router.push('/location-background')} />
      <Button variant="ghost" title={t('home.backgroundDismiss')} onPress={dismiss} />
    </View>
  );
}

const styles = StyleSheet.create({
  card: { backgroundColor: colors.infoSurface, borderRadius: radius.lg, padding: spacing.md, gap: spacing.sm },
});
