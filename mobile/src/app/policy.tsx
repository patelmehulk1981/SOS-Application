import * as WebBrowser from 'expo-web-browser';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Text } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { isApiError } from '@/api/errors';
import { acceptCurrentPolicy, signOut } from '@/auth/session';
import { env } from '@/config/env';
import { useAppInfo } from '@/features/appInfo';
import { Banner } from '@/ui/Banner';
import { Button } from '@/ui/Button';
import { Checkbox } from '@/ui/Checkbox';
import { describeError, type ErrorDescription } from '@/ui/describeError';
import { EmergencyCallBar } from '@/ui/EmergencyCallBar';
import { Screen } from '@/ui/Screen';
import { colors, typography } from '@/ui/theme';

/** Shown when App:PolicyVersion changed (403 policy.acceptance_required): re-consent before anything else. */
export default function PolicyScreen() {
  const { t } = useTranslation();
  const appInfo = useAppInfo();
  const [agreed, setAgreed] = useState(false);
  const [adult, setAdult] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ErrorDescription | null>(null);
  const version = appInfo.data?.policyVersion;

  const accept = async () => {
    if (!version) return;
    if (!agreed || !adult) {
      setError({ message: t('join.mustAgree') });
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await acceptCurrentPolicy(version);
    } catch (e) {
      if (isApiError(e, 'policy.version_mismatch')) {
        setAgreed(false);
        await appInfo.refetch();
      }
      setError(describeError(e, t));
    } finally {
      setBusy(false);
    }
  };

  return (
    <SafeAreaView style={{ flex: 1, backgroundColor: colors.background }} edges={['top']}>
      <Screen
        footer={
          <>
            <Button title={t('policy.accept')} onPress={accept} loading={busy} disabled={!version} />
            <Button variant="ghost" title={t('common.signOut')} onPress={() => void signOut()} disabled={busy} />
            <EmergencyCallBar />
          </>
        }
      >
        <Text style={typography.title} accessibilityRole="header">
          {t('policy.title')}
        </Text>
        {version ? <Text style={typography.body}>{t('policy.body', { version })}</Text> : null}
        <Button variant="ghost" title={t('join.readPrivacy')} onPress={() => void WebBrowser.openBrowserAsync(env.privacyUrl)} />
        <Button variant="ghost" title={t('join.readTerms')} onPress={() => void WebBrowser.openBrowserAsync(env.termsUrl)} />
        {version ? (
          <>
            <Checkbox label={t('join.agree', { version })} checked={agreed} onChange={setAgreed} />
            <Checkbox label={t('join.adult')} checked={adult} onChange={setAdult} />
          </>
        ) : appInfo.isError ? (
          <Button variant="secondary" title={t('common.retry')} onPress={() => void appInfo.refetch()} />
        ) : (
          <Text style={typography.small}>{t('common.loading')}</Text>
        )}
        {error ? <Banner tone="error" message={error.message} detail={error.reference} /> : null}
      </Screen>
    </SafeAreaView>
  );
}
