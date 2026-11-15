import { Image } from 'expo-image';
import * as Location from 'expo-location';
import { useRouter } from 'expo-router';
import * as WebBrowser from 'expo-web-browser';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ActivityIndicator, Alert, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { meApi } from '@/api/endpoints';
import { isApiError } from '@/api/errors';
import { signOut } from '@/auth/session';
import { env } from '@/config/env';
import { useAppInfo } from '@/features/appInfo';
import { useBackgroundLocationStore } from '@/features/location/background/controller';
import { getOneFix } from '@/features/location/useCurrentLocation';
import { exportMyData, profileKey, useProfile, useProfileMutation, useUpdateConsent, useUpdatePreferences } from '@/features/profile/queries';
import { deletePhotos, pickPhotos } from '@/features/sos/photos';
import { appVersion } from '@/lib/device';
import { formatDate } from '@/lib/format';
import { formatPhone } from '@/lib/phone';
import { Banner } from '@/ui/Banner';
import { Button } from '@/ui/Button';
import { describeError, type ErrorDescription } from '@/ui/describeError';
import { requestPushPermission } from '@/features/push/push';
import { usePushStore } from '@/features/push/pushStore';
import { InfoRow, LinkRow, Section, SwitchRow } from '@/ui/Rows';
import { colors, spacing, typography } from '@/ui/theme';
import { useQueryClient } from '@tanstack/react-query';

const AVATAR_SIZE = 88;

export default function ProfileScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const client = useQueryClient();
  const profile = useProfile();
  const appInfo = useAppInfo();
  const preferences = useUpdatePreferences();
  const consent = useUpdateConsent();
  const uploadPhoto = useProfileMutation((uri: string) => meApi.uploadPhoto({ uri, name: 'profile.jpg', type: 'image/jpeg' }));
  const removePhoto = useProfileMutation(() => meApi.removePhoto());

  const [notice, setNotice] = useState<(ErrorDescription & { tone: 'error' | 'success' }) | null>(null);
  const [busy, setBusy] = useState<'export' | 'delete' | 'signOut' | null>(null);

  const fail = (e: unknown) => setNotice({ tone: 'error', ...describeError(e, t) });

  if (profile.isPending) {
    return (
      <View style={styles.center}>
        <ActivityIndicator size="large" color={colors.primary} />
      </View>
    );
  }
  if (profile.isError || !profile.data) {
    return (
      <View style={[styles.center, styles.padded]}>
        <Banner tone="error" message={describeError(profile.error, t).message} />
        <Button variant="secondary" title={t('common.retry')} onPress={() => void profile.refetch()} />
      </View>
    );
  }

  const me = profile.data;

  const changePhoto = async () => {
    setNotice(null);
    const result = await pickPhotos('library', 1);
    if (result.kind !== 'picked' || !result.photos[0]) return;
    const uri = result.photos[0].uri;
    uploadPhoto.mutate(uri, { onError: fail, onSettled: () => deletePhotos([uri]) });
  };

  const setLocationSharing = async (granted: boolean) => {
    setNotice(null);
    if (granted) {
      // Consent is recorded only after the OS permission, so the switch never claims more than the phone allows.
      const permission = await Location.requestForegroundPermissionsAsync();
      if (!permission.granted) {
        setNotice({ tone: 'error', message: t('profile.locationDenied') });
        return;
      }
    }
    consent.mutate(
      { type: 'LocationSharing', granted },
      {
        onError: fail,
        onSuccess: () => {
          if (!granted) return;
          // First location so nearby alerts can work right away (background updates: docs/06).
          void getOneFix()
            .then((fix) => (fix ? meApi.updateLocation(fix) : undefined))
            .catch(() => {
              // Best effort.
            });
        },
      },
    );
  };

  const setHealthData = (granted: boolean) => {
    setNotice(null);
    if (granted) {
      consent.mutate({ type: 'HealthData', granted: true }, { onError: fail });
      return;
    }
    Alert.alert(t('profile.healthWithdrawTitle'), t('profile.healthWithdrawBody'), [
      { text: t('common.cancel'), style: 'cancel' },
      { text: t('profile.deleteConfirm'), style: 'destructive', onPress: () => consent.mutate({ type: 'HealthData', granted: false }, { onError: fail }) },
    ]);
  };

  const exportData = async () => {
    setBusy('export');
    setNotice(null);
    try {
      await exportMyData();
    } catch (e) {
      fail(e);
    } finally {
      setBusy(null);
    }
  };

  const deleteAccount = () => {
    Alert.alert(t('profile.deleteTitle'), t('profile.deleteBody'), [
      { text: t('common.cancel'), style: 'cancel' },
      {
        text: t('profile.deleteConfirm'),
        style: 'destructive',
        onPress: async () => {
          setBusy('delete');
          setNotice(null);
          try {
            await meApi.deleteAccount();
            client.removeQueries({ queryKey: profileKey });
            await signOut('account.deleted', { skipServer: true });
          } catch (e) {
            if (isApiError(e, 'account.open_sos')) setNotice({ tone: 'error', message: t('profile.deleteOpenSos') });
            else fail(e);
            setBusy(null);
          }
        },
      },
    ]);
  };

  return (
    <SafeAreaView style={styles.safe} edges={['bottom', 'left', 'right']}>
      <ScrollView contentContainerStyle={styles.content} contentInsetAdjustmentBehavior="automatic">
        <View style={styles.identity}>
          <Pressable
            accessibilityRole="button"
            accessibilityLabel={me.profilePhotoUrl ? t('profile.photo.change') : t('profile.photo.add')}
            onPress={() => void changePhoto()}
            disabled={uploadPhoto.isPending}
          >
            {me.profilePhotoUrl ? (
              <Image source={{ uri: me.profilePhotoUrl }} style={styles.avatar} contentFit="cover" />
            ) : (
              <View style={[styles.avatar, styles.avatarEmpty]}>
                <Text style={styles.avatarInitial}>{(me.fullName ?? '?').charAt(0).toUpperCase()}</Text>
              </View>
            )}
          </Pressable>
          <View style={styles.identityText}>
            <Text style={typography.heading}>{me.fullName}</Text>
            <Text style={typography.small}>{t(`common.roles.${me.role}`)}</Text>
            {uploadPhoto.isPending || removePhoto.isPending ? <ActivityIndicator color={colors.primary} /> : null}
            {me.profilePhotoUrl && !uploadPhoto.isPending ? (
              <Button variant="ghost" title={t('profile.photo.remove')} onPress={() => removePhoto.mutate(undefined, { onError: fail })} />
            ) : null}
          </View>
        </View>

        <Section>
          <InfoRow label={t('profile.phone')} value={formatPhone(me.phoneNumber)} />
          {me.referredBy ? (
            <InfoRow
              label={t('profile.invitedBy')}
              value={`${me.referredBy.fullName ?? ''} (${t(`common.roles.${me.referredBy.role}`)})`}
            />
          ) : null}
          <InfoRow value={t('profile.memberSince', { date: formatDate(me.createdAt) })} />
          <LinkRow title={t('profile.edit')} onPress={() => router.push('/profile/edit')} />
        </Section>

        {notice ? <Banner tone={notice.tone} message={notice.message} detail={notice.reference} /> : null}

        <Section title={t('profile.sections.alerts')}>
          <SwitchRow
            title={t('profile.alertsEnabled')}
            help={t('profile.alertsHelp')}
            value={me.alertsEnabled}
            disabled={preferences.isPending}
            onChange={(value) => preferences.mutate(value, { onError: fail })}
          />
          <SwitchRow
            title={t('profile.locationSharing')}
            help={t('profile.locationSharingHelp')}
            value={me.consents.locationSharing}
            disabled={consent.isPending}
            onChange={(value) => void setLocationSharing(value)}
          />
          <BackgroundLocationRow />
          <NotificationsRow />
        </Section>

        <Section title={t('profile.sections.privacy')}>
          <SwitchRow
            title={t('profile.healthData')}
            help={t('profile.healthDataHelp')}
            value={me.consents.healthData}
            disabled={consent.isPending}
            onChange={setHealthData}
          />
          <LinkRow title={t('profile.readPrivacy')} onPress={() => void WebBrowser.openBrowserAsync(env.privacyUrl)} />
          <LinkRow title={t('profile.readTerms')} onPress={() => void WebBrowser.openBrowserAsync(env.termsUrl)} />
          {appInfo.data?.grievanceContactEmail ? (
            <InfoRow value={t('profile.grievance', { email: appInfo.data.grievanceContactEmail })} />
          ) : null}
        </Section>

        <Section title={t('profile.sections.account')}>
          <LinkRow title={busy === 'export' ? t('common.loading') : t('profile.exportData')} onPress={() => void exportData()} />
          <LinkRow title={busy === 'delete' ? t('common.loading') : t('profile.deleteAccount')} onPress={deleteAccount} destructive />
        </Section>

        <Button
          variant="secondary"
          title={t('common.signOut')}
          loading={busy === 'signOut'}
          onPress={async () => {
            setBusy('signOut');
            try {
              await signOut();
            } finally {
              setBusy(null);
            }
          }}
        />
        <Text style={[typography.small, styles.centerText]}>{t('profile.version', { version: appVersion })}</Text>
      </ScrollView>
    </SafeAreaView>
  );
}

/** Background location state; opens the disclosure / control screen. */
function BackgroundLocationRow() {
  const { t } = useTranslation();
  const router = useRouter();
  const status = useBackgroundLocationStore((s) => s.status);
  if (status === 'unavailable') return <InfoRow label={t('profile.backgroundLocation')} value={t('profile.backgroundStatus.unavailable')} />;
  return (
    <LinkRow
      title={`${t('profile.backgroundLocation')}: ${t(`profile.backgroundStatus.${status}`)}`}
      onPress={() => router.push('/location-background')}
    />
  );
}

/** Notification permission at a glance; tapping asks for it (or opens Settings if it was refused). */
function NotificationsRow() {
  const { t } = useTranslation();
  const permission = usePushStore((s) => s.permission);
  if (permission === 'unavailable') return <InfoRow label={t('profile.notifications')} value={t('profile.notificationsUnavailable')} />;
  if (permission === 'granted') return <InfoRow label={t('profile.notifications')} value={t('profile.notificationsOn')} />;
  return (
    <LinkRow
      title={`${t('profile.notifications')}: ${t('profile.notificationsOff')}`}
      onPress={() => void requestPushPermission()}
    />
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.background },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: spacing.md, backgroundColor: colors.background },
  padded: { padding: spacing.lg },
  content: { padding: spacing.lg, gap: spacing.lg },
  identity: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  identityText: { flex: 1, gap: 2, alignItems: 'flex-start' },
  avatar: { width: AVATAR_SIZE, height: AVATAR_SIZE, borderRadius: AVATAR_SIZE / 2, backgroundColor: colors.surface },
  avatarEmpty: { alignItems: 'center', justifyContent: 'center', backgroundColor: colors.primary },
  avatarInitial: { color: colors.onPrimary, fontSize: 34, fontWeight: '700' },
  centerText: { textAlign: 'center' },
});
