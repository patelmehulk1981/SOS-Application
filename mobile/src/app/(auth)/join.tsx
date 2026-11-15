import { useQuery } from '@tanstack/react-query';
import * as Clipboard from 'expo-clipboard';
import * as WebBrowser from 'expo-web-browser';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { StyleSheet, Text, View } from 'react-native';

import { invitationsApi } from '@/api/endpoints';
import { isApiError } from '@/api/errors';
import { loginWithVerifiedPhone } from '@/auth/session';
import { useSignupStore } from '@/auth/signupStore';
import { env } from '@/config/env';
import { useAppInfo } from '@/features/appInfo';
import { extractInviteCode, isWellFormedInviteCode, normalizeInviteCode } from '@/lib/inviteCode';
import { Banner } from '@/ui/Banner';
import { Button } from '@/ui/Button';
import { Checkbox } from '@/ui/Checkbox';
import { describeError, type ErrorDescription } from '@/ui/describeError';
import { Screen } from '@/ui/Screen';
import { TextField } from '@/ui/TextField';
import { colors, radius, spacing, typography } from '@/ui/theme';

/**
 * First-time registration (422 auth.registration_required): invite code + Privacy Notice consent
 * + 18+ declaration (DPDP Act s.5/s.6/s.9; docs/01-architecture.md §6.2.1).
 */
export default function JoinScreen() {
  const { t } = useTranslation();
  const appInfo = useAppInfo();
  const pendingInviteCode = useSignupStore((s) => s.pendingInviteCode);
  const resetSignup = useSignupStore((s) => s.reset);

  const [code, setCode] = useState(pendingInviteCode ?? '');
  const [agreed, setAgreed] = useState(false);
  const [adult, setAdult] = useState(false);
  const [codeError, setCodeError] = useState<string | null>(null);
  const [error, setError] = useState<ErrorDescription | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const normalized = normalizeInviteCode(code);
  const wellFormed = isWellFormedInviteCode(normalized);

  const preview = useQuery({
    queryKey: ['invitation-preview', normalized],
    queryFn: () => invitationsApi.preview(normalized),
    enabled: wellFormed,
    staleTime: 60_000,
    retry: false,
  });

  const policyVersion = appInfo.data?.policyVersion;
  const inviteUsable = preview.data?.status === 'Active';

  const paste = async () => {
    const text = await Clipboard.getStringAsync();
    const extracted = extractInviteCode(text);
    if (extracted) {
      setCode(extracted);
      setCodeError(null);
    } else {
      setCodeError(t('join.pasteEmpty'));
    }
  };

  const submit = async () => {
    setError(null);
    if (!wellFormed) {
      setCodeError(t('join.codeInvalid'));
      return;
    }
    if (!agreed || !adult || !policyVersion) {
      setError({ message: t('join.mustAgree') });
      return;
    }
    setSubmitting(true);
    try {
      await loginWithVerifiedPhone({
        inviteCode: normalized,
        consent: { policyVersion, confirmedAdult: true },
      });
      resetSignup();
      // Signed in: the root guard moves on to profile setup.
    } catch (e) {
      if (isApiError(e, 'invitation.not_found')) {
        setCodeError(t('join.notFound'));
      } else if (isApiError(e, 'invitation.unusable')) {
        setCodeError(t('join.unusable'));
      } else if (isApiError(e, 'policy.version_mismatch')) {
        // The notice changed while this screen was open: show the new version and ask again.
        setAgreed(false);
        await appInfo.refetch();
        setError(describeError(e, t));
      } else {
        setError(describeError(e, t));
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Screen
      footer={
        <Button
          title={t('join.submit')}
          onPress={submit}
          loading={submitting}
          disabled={!policyVersion || (preview.isSuccess && !inviteUsable)}
        />
      }
    >
      <Text style={typography.title} accessibilityRole="header">
        {t('join.title')}
      </Text>
      <Text style={typography.body}>{t('join.intro')}</Text>

      <TextField
        label={t('join.codeLabel')}
        value={code}
        onChangeText={(text) => {
          setCode(text.toUpperCase());
          setCodeError(null);
        }}
        autoCapitalize="characters"
        autoCorrect={false}
        maxLength={16}
        hint={t('join.codeHint')}
        error={codeError}
      />
      <Button variant="secondary" title={t('join.paste')} onPress={paste} />

      {preview.isFetching ? <Text style={typography.small}>{t('join.checking')}</Text> : null}
      {preview.data && inviteUsable ? (
        <Banner
          tone="success"
          message={t('join.invitedBy', {
            name: preview.data.invitedBy?.fullName ?? t(`common.roles.${preview.data.invitedBy?.role ?? 'SubAdmin'}`),
            inviterRole: t(`common.roles.${preview.data.invitedBy?.role ?? 'SubAdmin'}`),
            role: t(`common.roles.${preview.data.inviteeRole}`),
          })}
        />
      ) : null}
      {preview.data && !inviteUsable ? <Banner tone="error" message={t('join.unusable')} /> : null}
      {isApiError(preview.error, 'invitation.not_found') ? <Banner tone="error" message={t('join.notFound')} /> : null}

      <View style={styles.notice}>
        <Text style={typography.heading}>{t('join.noticeTitle')}</Text>
        {(['phone', 'sos', 'responders', 'location'] as const).map((point) => (
          <Text key={point} style={typography.body}>
            {'• '}
            {t(`join.noticePoints.${point}`)}
          </Text>
        ))}
        <View style={styles.links}>
          <Button variant="ghost" title={t('join.readPrivacy')} onPress={() => void WebBrowser.openBrowserAsync(env.privacyUrl)} />
          <Button variant="ghost" title={t('join.readTerms')} onPress={() => void WebBrowser.openBrowserAsync(env.termsUrl)} />
        </View>
      </View>

      {appInfo.isError && !policyVersion ? (
        <>
          <Banner tone="error" message={describeError(appInfo.error, t).message} />
          <Button variant="secondary" title={t('common.retry')} onPress={() => void appInfo.refetch()} />
        </>
      ) : null}
      {policyVersion ? (
        <View style={styles.checks}>
          <Checkbox label={t('join.agree', { version: policyVersion })} checked={agreed} onChange={setAgreed} />
          <Checkbox label={t('join.adult')} checked={adult} onChange={setAdult} />
        </View>
      ) : null}

      {error ? <Banner tone="error" message={error.message} detail={error.reference} /> : null}
    </Screen>
  );
}

const styles = StyleSheet.create({
  notice: { backgroundColor: colors.surface, borderRadius: radius.md, padding: spacing.md, gap: spacing.sm },
  links: { alignItems: 'flex-start' },
  checks: { gap: spacing.sm },
});
