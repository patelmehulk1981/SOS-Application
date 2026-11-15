import { Stack, useRouter } from 'expo-router';
import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import { isAdminRole, useSessionStore } from '@/auth/sessionStore';
import { useBackgroundLocationSync } from '@/features/location/background/controller';
import { useLocationHeartbeat } from '@/features/location/heartbeat';
import { useProfile } from '@/features/profile/queries';
import { InAppNotice } from '@/features/push/InAppNotice';
import { routeFor } from '@/features/push/payload';
import { startPush } from '@/features/push/push';
import { usePushStore } from '@/features/push/pushStore';
import { resumeOutbox } from '@/features/sos/sosOutbox';
import { colors } from '@/ui/theme';

export const unstable_settings = { anchor: 'index' };

/** The signed-in area. Mounted only while signed in (root guard), so effects here are per session. */
export default function AppLayout() {
  const { t } = useTranslation();
  const router = useRouter();
  const isAdmin = useSessionStore((s) => isAdminRole(s.user));
  const pendingOpen = usePushStore((s) => s.pendingOpen);
  const profile = useProfile();

  useEffect(() => {
    // An SOS left undelivered by a previous run (offline, or the app was killed) is sent now.
    void resumeOutbox().catch(() => {
      // Storage unreadable: nothing to resume.
    });
  }, []);

  // Push: permission state, installation-id registration, foreground messages and notification taps.
  useEffect(() => startPush(), []);

  // A tapped notification (app in background or launched by the tap) opens its SOS once signed in.
  useEffect(() => {
    if (!pendingOpen) return;
    usePushStore.setState({ pendingOpen: null });
    router.push(routeFor(pendingOpen));
  }, [pendingOpen, router]);

  // Fresh last-known location for neighbour alerts while the app is in use.
  useLocationHeartbeat(profile.data?.consents.locationSharing === true);
  // Background location (if turned on): restart what the OS dropped, stop it if consent or permission is gone.
  useBackgroundLocationSync(profile.data?.consents.locationSharing);

  return (
    <View style={styles.flex}>
      <Stack
        screenOptions={{
          headerShadowVisible: false,
          headerTransparent: false,
          headerTintColor: colors.text,
          headerStyle: { backgroundColor: colors.background },
          headerBackButtonDisplayMode: 'minimal',
          contentStyle: { backgroundColor: colors.background },
        }}
      >
        <Stack.Screen name="index" options={{ headerShown: false }} />
        <Stack.Screen name="sos/new" options={{ title: t('sos.new.title'), presentation: 'fullScreenModal' }} />
        <Stack.Screen name="sos/[id]" options={{ title: t('sos.detail.title') }} />
        <Stack.Screen name="sos/index" options={{ title: t('sos.history.title') }} />
        <Stack.Screen name="alerts/index" options={{ title: t('alerts.title') }} />
        <Stack.Screen name="profile/index" options={{ title: t('profile.title') }} />
        <Stack.Screen name="profile/edit" options={{ title: t('profileEdit.title') }} />
        <Stack.Screen name="location-background" options={{ title: t('backgroundLocation.title') }} />
        {/* Admin area: Sub-Admins and the Master Admin only (the API enforces the same, with 404s outside scope). */}
        <Stack.Protected guard={isAdmin}>
          <Stack.Screen name="admin/index" options={{ title: t('admin.hub.title') }} />
          <Stack.Screen name="admin/queue" options={{ title: t('admin.queue.title') }} />
          <Stack.Screen name="admin/sos" options={{ title: t('admin.sosList.title') }} />
          <Stack.Screen name="admin/map" options={{ title: t('admin.map.title') }} />
          <Stack.Screen name="admin/invitations/index" options={{ title: t('admin.invitations.title') }} />
          <Stack.Screen name="admin/invitations/new" options={{ title: t('admin.newInvitation.title'), presentation: 'modal' }} />
          <Stack.Screen name="admin/users/index" options={{ title: t('admin.users.title') }} />
          <Stack.Screen name="admin/users/[id]" options={{ title: t('admin.user.title') }} />
          <Stack.Screen name="admin/referrals" options={{ title: t('admin.referrals.title') }} />
        </Stack.Protected>
      </Stack>
      <InAppNotice />
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1, backgroundColor: colors.background },
});
