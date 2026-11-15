import { useRouter } from 'expo-router';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Linking, Platform, ScrollView, StyleSheet, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import {
  disableBackgroundLocation,
  enableBackgroundLocation,
  syncBackgroundLocation,
  useBackgroundLocationStore,
} from '@/features/location/background/controller';
import { useProfile } from '@/features/profile/queries';
import { Banner } from '@/ui/Banner';
import { Button } from '@/ui/Button';
import { colors, radius, spacing, typography } from '@/ui/theme';

/**
 * Prominent disclosure before the background-location permission (Google Play User Data policy;
 * Apple guideline 5.1.1): what is collected, that it happens while the app is closed, why, what is
 * kept, and how to stop. The system prompt is shown only after the person taps Continue.
 * Also the place to see the state and turn it off.
 */
export default function BackgroundLocationScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const profile = useProfile();
  const status = useBackgroundLocationStore((s) => s.status);
  const permission = useBackgroundLocationStore((s) => s.permission);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ tone: 'error' | 'success' | 'info'; text: string } | null>(null);

  const locationSharing = profile.data?.consents.locationSharing;

  // Fresh state when the screen opens and after returning from Settings.
  useEffect(() => {
    if (locationSharing !== undefined) void syncBackgroundLocation(locationSharing).catch(() => undefined);
  }, [locationSharing]);

  const turnOn = async () => {
    setBusy(true);
    setMessage(null);
    try {
      const result = await enableBackgroundLocation(locationSharing === true);
      if (result === 'failed') setMessage({ tone: 'error', text: t('backgroundLocation.failed') });
    } finally {
      setBusy(false);
    }
  };

  const turnOff = async () => {
    setBusy(true);
    try {
      await disableBackgroundLocation();
      setMessage({ tone: 'info', text: t('backgroundLocation.turnedOff') });
    } finally {
      setBusy(false);
    }
  };

  const settingsOnly = status === 'needsPermission' || (permission !== null && permission.status !== 'granted' && !permission.canAsk);

  return (
    <SafeAreaView style={styles.safe} edges={['bottom', 'left', 'right']}>
      <ScrollView contentContainerStyle={styles.content} contentInsetAdjustmentBehavior="automatic">
        {status === 'on' ? (
          <View style={styles.state}>
            <Text style={typography.heading}>{t('backgroundLocation.onTitle')}</Text>
            <Text style={typography.body}>{t('backgroundLocation.onBody')}</Text>
          </View>
        ) : (
          <Text style={typography.title} accessibilityRole="header">
            {t('backgroundLocation.heading')}
          </Text>
        )}

        <Text style={typography.body}>{t('backgroundLocation.lead')}</Text>

        <Point title={t('backgroundLocation.whatTitle')} body={t('backgroundLocation.what')} />
        <Point title={t('backgroundLocation.whyTitle')} body={t('backgroundLocation.why')} />
        <Point title={t('backgroundLocation.keptTitle')} body={t('backgroundLocation.kept')} />
        <Point title={t('backgroundLocation.controlTitle')} body={t('backgroundLocation.control')} />
        <Text style={typography.small}>{t('backgroundLocation.batteryNote')}</Text>

        {message ? <Banner tone={message.tone} message={message.text} /> : null}
        {status === 'needsSharing' ? <Banner tone="info" message={t('backgroundLocation.needsSharingBody')} /> : null}
        {status === 'needsPermission' ? <Banner tone="info" message={t('backgroundLocation.needsPermissionBody')} /> : null}
        {status === 'unavailable' ? <Banner tone="info" message={t('backgroundLocation.unavailableBody')} /> : null}

        {status === 'on' ? (
          <Button variant="secondary" title={t('backgroundLocation.turnOff')} loading={busy} onPress={() => void turnOff()} />
        ) : status === 'needsSharing' ? (
          <Button title={t('home.profile')} onPress={() => router.replace('/profile')} />
        ) : status === 'unavailable' ? null : (
          <>
            {settingsOnly ? null : (
              <Text style={typography.small}>{Platform.OS === 'android' ? t('backgroundLocation.stepAndroid') : t('backgroundLocation.stepIos')}</Text>
            )}
            {settingsOnly ? (
              <Button title={t('backgroundLocation.openSettings')} onPress={() => void Linking.openSettings()} />
            ) : (
              <Button title={t('backgroundLocation.turnOn')} loading={busy} onPress={() => void turnOn()} />
            )}
            {status === 'needsPermission' ? (
              <Button variant="ghost" title={t('backgroundLocation.turnOff')} disabled={busy} onPress={() => void turnOff()} />
            ) : (
              <Button variant="ghost" title={t('backgroundLocation.notNow')} disabled={busy} onPress={() => router.back()} />
            )}
          </>
        )}
      </ScrollView>
    </SafeAreaView>
  );
}

function Point({ title, body }: { title: string; body: string }) {
  return (
    <View style={styles.point}>
      <Text style={typography.label}>{title}</Text>
      <Text style={typography.body}>{body}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.background },
  content: { padding: spacing.lg, gap: spacing.md },
  state: { backgroundColor: colors.successSurface, borderRadius: radius.lg, padding: spacing.md, gap: spacing.xs },
  point: { gap: 2 },
});
