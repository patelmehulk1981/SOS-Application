import '@/i18n';

import { QueryClientProvider, focusManager } from '@tanstack/react-query';
import { Stack } from 'expo-router';
import * as SplashScreen from 'expo-splash-screen';
import { StatusBar } from 'expo-status-bar';
import { useEffect } from 'react';
import { AppState, Platform } from 'react-native';
import { SafeAreaProvider } from 'react-native-safe-area-context';

import { queryClient } from '@/api/queryClient';
import { bootstrapSession } from '@/auth/session';
import { useSessionStore } from '@/auth/sessionStore';
import { isUpdateRequired, useAppInfo } from '@/features/appInfo';
import { colors } from '@/ui/theme';

void SplashScreen.preventAutoHideAsync();

// Refetch stale queries when the app returns to the foreground (React Native has no window focus).
focusManager.setEventListener((setFocused) => {
  const subscription = AppState.addEventListener('change', (state) => {
    if (Platform.OS !== 'web') setFocused(state === 'active');
  });
  return () => subscription.remove();
});

export default function RootLayout() {
  return (
    <QueryClientProvider client={queryClient}>
      <SafeAreaProvider>
        <StatusBar style="dark" />
        <RootNavigator />
      </SafeAreaProvider>
    </QueryClientProvider>
  );
}

/**
 * Route access is decided here, in one place, from the session state:
 *   update required → update-required
 *   signed out      → (auth) welcome / phone / otp / join
 *   policy changed  → policy
 *   no name yet     → profile-setup
 *   otherwise       → (app)
 * Expo Router redirects automatically when a guard turns false.
 */
function RootNavigator() {
  const status = useSessionStore((s) => s.status);
  const user = useSessionStore((s) => s.user);
  const appInfo = useAppInfo();

  useEffect(() => {
    bootstrapSession().catch(() => {
      // Secure storage unreadable (rare, e.g. keystore reset): start signed out rather than hang on the splash.
      useSessionStore.setState({ status: 'signedOut', user: null });
    });
  }, []);

  useEffect(() => {
    if (status !== 'loading') SplashScreen.hide();
  }, [status]);

  if (status === 'loading') return null;

  const updateRequired = isUpdateRequired(appInfo.data);
  const signedIn = status === 'signedIn' && user !== null;
  const policyRequired = signedIn && user.policyAcceptanceRequired;
  const needsProfile = signedIn && !policyRequired && !user.profileCompleted;

  return (
    <Stack
      screenOptions={{
        headerShown: false,
        contentStyle: { backgroundColor: colors.background },
      }}
    >
      <Stack.Protected guard={updateRequired}>
        <Stack.Screen name="update-required" />
      </Stack.Protected>
      <Stack.Protected guard={!updateRequired && !signedIn}>
        <Stack.Screen name="(auth)" />
      </Stack.Protected>
      <Stack.Protected guard={!updateRequired && policyRequired}>
        <Stack.Screen name="policy" />
      </Stack.Protected>
      <Stack.Protected guard={!updateRequired && needsProfile}>
        <Stack.Screen name="profile-setup" />
      </Stack.Protected>
      <Stack.Protected guard={!updateRequired && signedIn && !policyRequired && !needsProfile}>
        <Stack.Screen name="(app)" />
      </Stack.Protected>
      {/* Invite links work in every state: they store the code and redirect. */}
      <Stack.Screen name="i/[code]" />
    </Stack>
  );
}
