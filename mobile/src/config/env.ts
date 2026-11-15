import Constants from 'expo-constants';
import { Platform } from 'react-native';

/**
 * Runtime configuration. EXPO_PUBLIC_* variables are inlined into the bundle at build time,
 * so they must be read with literal `process.env.EXPO_PUBLIC_X` expressions.
 */

export type AuthMode = 'firebase' | 'dev';
export type AppVariant = 'development' | 'preview' | 'production';

const rawApiBaseUrl = process.env.EXPO_PUBLIC_API_BASE_URL;
const rawAuthMode = process.env.EXPO_PUBLIC_AUTH_MODE;

function resolveAuthMode(value: string | undefined): AuthMode {
  // The development OTP bypass is never honoured in a release build, whatever the build env says.
  if (value === 'dev' && __DEV__) return 'dev';
  return 'firebase';
}

function resolveApiBaseUrl(value: string | undefined): string {
  if (!value) {
    throw new Error('EXPO_PUBLIC_API_BASE_URL is not set. Copy mobile/.env.example to mobile/.env.local.');
  }
  const trimmed = value.replace(/\/+$/, '');
  if (!__DEV__ && !trimmed.startsWith('https://')) {
    throw new Error('Release builds must use an https:// API URL.');
  }
  return trimmed;
}

const extra = (Constants.expoConfig?.extra ?? {}) as {
  variant?: AppVariant;
  firebase?: boolean;
  maps?: { android?: boolean; ios?: boolean };
};

/** The native Google Maps SDK has an API key in this build (app.config.ts). Without one, the admin map falls back to a list. */
const mapsConfigured = Platform.OS === 'ios' ? Boolean(extra.maps?.ios) : Platform.OS === 'android' ? Boolean(extra.maps?.android) : false;

export const env = {
  apiBaseUrl: resolveApiBaseUrl(rawApiBaseUrl),
  authMode: resolveAuthMode(rawAuthMode),
  privacyUrl: process.env.EXPO_PUBLIC_PRIVACY_URL ?? 'https://sos.example.com/privacy',
  termsUrl: process.env.EXPO_PUBLIC_TERMS_URL ?? 'https://sos.example.com/terms',
  variant: extra.variant ?? 'development',
  mapsConfigured,
  /** Firebase native config is in this build: push notifications (and real phone sign-in) are available. */
  pushAvailable: Boolean(extra.firebase) && (Platform.OS === 'android' || Platform.OS === 'ios'),
  /** Development-mode OTP accepted by the dev phone-auth provider. */
  devOtp: '123456',
} as const;
