import type { ConfigContext, ExpoConfig } from 'expo/config';

/**
 * Build-time app configuration. Values come from environment variables so one codebase builds
 * the development, preview (staging) and production apps. See mobile/README.md.
 *
 *   APP_VARIANT                 development | preview | production (default: development)
 *   INVITE_LINK_HOST            Host of the invite links, e.g. sos.example.org (must equal the API's App:InviteLinkBaseUrl host)
 *   GOOGLE_SERVICES_JSON        Path to google-services.json (Android Firebase config; an EAS file secret in CI)
 *   GOOGLE_SERVICE_INFO_PLIST   Path to GoogleService-Info.plist (iOS Firebase config)
 *   GOOGLE_MAPS_ANDROID_API_KEY Maps SDK for Android key (admin SOS map), restricted to the package + signing SHA-1
 *   GOOGLE_MAPS_IOS_API_KEY     Maps SDK for iOS key, restricted to the bundle id
 *   EXPO_PUBLIC_*               Read by the JavaScript bundle at runtime (src/config/env.ts)
 */

type Variant = 'development' | 'preview' | 'production';

const variant = parseVariant(process.env.APP_VARIANT);
const inviteHost = process.env.INVITE_LINK_HOST ?? 'sos.example.com';
const googleServicesJson = process.env.GOOGLE_SERVICES_JSON;
const googleServiceInfoPlist = process.env.GOOGLE_SERVICE_INFO_PLIST;
const hasFirebaseConfig = Boolean(googleServicesJson && googleServiceInfoPlist);
const mapsAndroidKey = process.env.GOOGLE_MAPS_ANDROID_API_KEY || undefined;
const mapsIosKey = process.env.GOOGLE_MAPS_IOS_API_KEY || undefined;

// Replace with your organisation's reverse-DNS id before the first store upload; it cannot change afterwards.
const baseBundleId = 'com.communitysos.app';
const bundleId = variant === 'production' ? baseBundleId : `${baseBundleId}.${variant === 'preview' ? 'preview' : 'dev'}`;
const appName = variant === 'production' ? 'Community SOS' : variant === 'preview' ? 'Community SOS (Preview)' : 'Community SOS (Dev)';

if (variant !== 'development' && !hasFirebaseConfig) {
  throw new Error(`APP_VARIANT=${variant} requires GOOGLE_SERVICES_JSON and GOOGLE_SERVICE_INFO_PLIST (Firebase phone sign-in).`);
}
if (variant !== 'development' && !(mapsAndroidKey && mapsIosKey)) {
  throw new Error(`APP_VARIANT=${variant} requires GOOGLE_MAPS_ANDROID_API_KEY and GOOGLE_MAPS_IOS_API_KEY (admin SOS map).`);
}

export default ({ config }: ConfigContext): ExpoConfig => ({
  ...config,
  name: appName,
  slug: 'community-sos',
  scheme: 'communitysos',
  version: '1.0.0',
  runtimeVersion: { policy: 'appVersion' },
  orientation: 'portrait',
  icon: './assets/icon.png',
  userInterfaceStyle: 'light',
  ios: {
    bundleIdentifier: bundleId,
    supportsTablet: false,
    associatedDomains: [`applinks:${inviteHost}`],
    ...(googleServiceInfoPlist ? { googleServicesFile: googleServiceInfoPlist } : {}),
    infoPlist: {
      ITSAppUsesNonExemptEncryption: false,
      // Development builds talk to the API over plain HTTP on the local network.
      ...(variant === 'development' ? { NSAppTransportSecurity: { NSAllowsLocalNetworking: true } } : {}),
    },
  },
  android: {
    package: bundleId,
    adaptiveIcon: {
      backgroundColor: '#FFFFFF',
      foregroundImage: './assets/android-icon-foreground.png',
      backgroundImage: './assets/android-icon-background.png',
      monochromeImage: './assets/android-icon-monochrome.png',
    },
    predictiveBackGestureEnabled: false,
    ...(googleServicesJson ? { googleServicesFile: googleServicesJson } : {}),
    // Location (foreground and background), camera and notifications (POST_NOTIFICATIONS) come from their config plugins below.
    // Photos are written only inside the app's own folder, so shared-storage write access is never needed.
    blockedPermissions: [
      'android.permission.RECORD_AUDIO',
      'android.permission.SYSTEM_ALERT_WINDOW',
      'android.permission.WRITE_EXTERNAL_STORAGE',
    ],
    intentFilters: [
      {
        // Android App Link: https://<host>/i/<code> opens the app (needs /.well-known/assetlinks.json on the host).
        action: 'VIEW',
        autoVerify: true,
        data: [{ scheme: 'https', host: inviteHost, pathPrefix: '/i/' }],
        category: ['BROWSABLE', 'DEFAULT'],
      },
    ],
  },
  plugins: [
    'expo-router',
    'expo-secure-store',
    'expo-localization',
    'expo-web-browser',
    [
      'expo-location',
      {
        locationWhenInUsePermission:
          'Community SOS uses your location to send it with your SOS, so your admin and nearby responders can find you.',
        // Background location (docs/06): asked only after the in-app disclosure, never at launch.
        locationAlwaysAndWhenInUsePermission:
          'Community SOS uses your location in the background, even when the app is closed, to alert you when someone within 5 km needs help. Only your latest location is kept.',
        isAndroidBackgroundLocationEnabled: true,
        isIosBackgroundLocationEnabled: true,
        // No location foreground service: no permanent notification and no FOREGROUND_SERVICE_LOCATION
        // declaration. Background fixes come from batched updates, a geofence and a periodic check-in.
        isAndroidForegroundServiceEnabled: false,
      },
    ],
    // Periodic check-in (WorkManager / BGTaskScheduler "processing") so a stationary phone stays fresh.
    'expo-background-task',
    [
      'expo-image-picker',
      {
        cameraPermission: 'Community SOS uses the camera so you can add photos of the emergency to your SOS.',
        photosPermission: 'Community SOS lets you choose photos of the emergency to add to your SOS.',
        microphonePermission: false,
      },
    ],
    [
      'expo-splash-screen',
      { image: './assets/icon.png', imageWidth: 160, resizeMode: 'contain', backgroundColor: '#FFFFFF' },
    ],
    [
      'expo-build-properties',
      {
        // React Native Firebase v27 is Swift Package Manager based and needs dynamic frameworks on iOS.
        ios: hasFirebaseConfig ? { useFrameworks: 'dynamic' } : {},
        android: { usesCleartextTraffic: variant === 'development' },
      },
    ],
    [
      'expo-notifications',
      {
        // Small status-bar icon (white on transparent) and the SOS red accent.
        icon: './assets/android-icon-monochrome.png',
        color: '#B71C1C',
        // Used by FCM when a message names no channel; the app creates all three channels at start.
        defaultChannel: 'sos-alert',
        // APNs environment: development builds use the sandbox; preview (ad hoc) and store builds production.
        mode: variant === 'development' ? 'development' : 'production',
        enableBackgroundRemoteNotifications: true,
      },
    ],
    ...(hasFirebaseConfig
      ? [
          '@react-native-firebase/app',
          '@react-native-firebase/auth',
          // Push is addressed by Firebase Installation ID (FCM registration tokens are deprecated since 2026).
          ['@react-native-firebase/messaging', { installationIdEnabled: true }] as [string, object],
        ]
      : []),
    // Google Maps on both platforms. Without a key the Android SDK crashes on first render, so the plugin
    // (and the map screen, via extra.maps) is enabled per platform only when its key is configured.
    ...(mapsAndroidKey || mapsIosKey
      ? [['react-native-maps', { androidGoogleMapsApiKey: mapsAndroidKey, iosGoogleMapsApiKey: mapsIosKey }] as [string, object]]
      : []),
  ],
  experiments: { typedRoutes: true },
  extra: { variant, firebase: hasFirebaseConfig, maps: { android: Boolean(mapsAndroidKey), ios: Boolean(mapsIosKey) } },
});

function parseVariant(value: string | undefined): Variant {
  if (value === undefined || value === '') return 'development';
  if (value === 'development' || value === 'preview' || value === 'production') return value;
  throw new Error(`Unknown APP_VARIANT "${value}". Use development, preview or production.`);
}
