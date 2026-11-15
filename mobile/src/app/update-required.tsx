import * as Application from 'expo-application';
import { useTranslation } from 'react-i18next';
import { Linking, Platform, Text } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { EmergencyNumbersList } from '@/features/EmergencyNumbersList';
import { Button } from '@/ui/Button';
import { Screen } from '@/ui/Screen';
import { colors, typography } from '@/ui/theme';

/** Blocking screen when this build is below App:MinimumAppVersion. Emergency numbers stay one tap away. */
export default function UpdateRequiredScreen() {
  const { t } = useTranslation();

  const openStore = () => {
    // Replace the App Store id once the app is listed.
    const url =
      Platform.OS === 'ios'
        ? 'itms-apps://apps.apple.com/app/id0000000000'
        : `market://details?id=${Application.applicationId ?? ''}`;
    void Linking.openURL(url).catch(() =>
      Linking.openURL(`https://play.google.com/store/apps/details?id=${Application.applicationId ?? ''}`),
    );
  };

  return (
    <SafeAreaView style={{ flex: 1, backgroundColor: colors.background }} edges={['top']}>
      <Screen footer={<Button title={t('update.action')} onPress={openStore} />}>
        <Text style={typography.title} accessibilityRole="header">
          {t('update.title')}
        </Text>
        <Text style={typography.body}>{t('update.body')}</Text>
        <EmergencyNumbersList />
      </Screen>
    </SafeAreaView>
  );
}
