import { useQuery } from '@tanstack/react-query';
import { useRouter } from 'expo-router';
import { useTranslation } from 'react-i18next';
import { StyleSheet, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { invitationsApi } from '@/api/endpoints';
import { useSessionStore } from '@/auth/sessionStore';
import { useSignupStore } from '@/auth/signupStore';
import { Banner } from '@/ui/Banner';
import { Button } from '@/ui/Button';
import { EmergencyCallBar } from '@/ui/EmergencyCallBar';
import { Screen } from '@/ui/Screen';
import { colors, spacing, typography } from '@/ui/theme';

export default function WelcomeScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const endedReason = useSessionStore((s) => s.endedReason);
  const pendingInviteCode = useSignupStore((s) => s.pendingInviteCode);

  const invite = useQuery({
    queryKey: ['invitation-preview', pendingInviteCode],
    queryFn: () => invitationsApi.preview(pendingInviteCode!),
    enabled: pendingInviteCode !== null,
    staleTime: 5 * 60_000,
  });

  const endedMessage = endedReason === null ? null : t(sessionEndedKey(endedReason));

  return (
    <SafeAreaView style={styles.safe} edges={['top']}>
      <Screen
        footer={
          <>
            <Button title={t('welcome.start')} onPress={() => router.push('/phone')} />
            <EmergencyCallBar />
          </>
        }
      >
        <View style={styles.hero}>
          <View style={styles.badge} accessible={false}>
            <Text style={styles.badgeText}>SOS</Text>
          </View>
          <Text style={typography.title} accessibilityRole="header">
            {t('common.appName')}
          </Text>
          <Text style={[typography.body, styles.center]}>{t('welcome.tagline')}</Text>
          <Text style={typography.small}>{t('common.adultsOnly')}</Text>
        </View>

        {endedMessage ? <Banner tone="info" message={endedMessage} /> : null}

        {invite.data?.status === 'Active' && invite.data.invitedBy ? (
          <Banner
            tone="success"
            message={t('welcome.invitedBy', {
              name: invite.data.invitedBy.fullName ?? t(`common.roles.${invite.data.invitedBy.role}`),
              role: t(`common.roles.${invite.data.inviteeRole}`),
            })}
          />
        ) : null}
        {invite.data && invite.data.status !== 'Active' ? <Banner tone="error" message={t('welcome.inviteInvalid')} /> : null}
        {invite.isError ? <Banner tone="error" message={t('welcome.inviteInvalid')} /> : null}
      </Screen>
    </SafeAreaView>
  );
}

/** Server codes (and local reasons) that end a session → message keys. */
function sessionEndedKey(reason: string) {
  switch (reason) {
    case 'auth.refresh_reused':
      return 'sessionEnded.refreshReused' as const;
    case 'auth.account_inactive':
      return 'sessionEnded.accountInactive' as const;
    case 'auth.refresh_expired':
      return 'sessionEnded.refreshExpired' as const;
    case 'account.deleted':
      return 'sessionEnded.accountDeleted' as const;
    default:
      return 'sessionEnded.default' as const;
  }
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.background },
  hero: { alignItems: 'center', gap: spacing.md, paddingTop: spacing.xl },
  badge: {
    width: 112,
    height: 112,
    borderRadius: 56,
    backgroundColor: colors.primary,
    alignItems: 'center',
    justifyContent: 'center',
  },
  badgeText: { color: colors.onPrimary, fontSize: 34, fontWeight: '800', letterSpacing: 2 },
  center: { textAlign: 'center' },
});
