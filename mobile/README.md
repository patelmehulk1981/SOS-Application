# Community SOS: mobile app

Expo SDK 58 (React Native 0.88, React 19.3, TypeScript 6), Expo Router, React Native Firebase v27.
Design and decisions: [docs/04a-mobile-foundation.md](../docs/04a-mobile-foundation.md).

## Run locally (no Firebase needed)

```powershell
# 1. API, reachable from the emulator/phone over HTTP
cd backend
dotnet run --project src/CommunitySos.Api -- --migrate                       # once
dotnet run --project src/CommunitySos.Api --launch-profile mobile-dev         # http://0.0.0.0:5180

# 2. App
cd mobile
npm ci
copy .env.example .env.local      # EXPO_PUBLIC_AUTH_MODE=dev, API at http://10.0.2.2:5180 (Android emulator)
npx expo start                    # press "a" for Android, or scan the QR code with a dev build
```

- In `dev` auth mode no SMS is sent. Use any number and the code **123456**. Sign in with the seeded Master Admin (`Seed:MasterAdmin:PhoneNumber`, `+910000000001` in Development), create an invite, and register a second number with that code.
- A physical phone needs `EXPO_PUBLIC_API_BASE_URL=http://<PC LAN IP>:5180`, and Windows Firewall must allow port 5180.
- The app uses native modules (secure store, and Firebase when configured), so build a **development client** once: `npx expo run:android`, or `npx eas-cli build --profile development`. Firebase is loaded lazily, so Expo Go should also run the app in `dev` auth mode, but that hasn't been tried. The development client is the supported path.

## Real phone sign-in (Firebase)

1. In the Firebase project, add an Android app (`com.communitysos.app.dev` / `.preview` / production id) with the debug and release **SHA-256** fingerprints, and an iOS app with the same bundle ids. Enable **Phone** sign-in.
2. Put `google-services.json` and `GoogleService-Info.plist` in `mobile/firebase/` (git-ignored), and set `GOOGLE_SERVICES_JSON` and `GOOGLE_SERVICE_INFO_PLIST` in `.env.local`. For EAS builds, upload them as **file environment variables** instead.
3. Set `EXPO_PUBLIC_AUTH_MODE=firebase` and rebuild the dev client (`npx expo prebuild --clean`, then `npx expo run:android`).
4. On the API, set `Firebase:ProjectId` and `Firebase:ServiceAccountJson`, and turn off `UseDevelopmentVerifier`.

Firebase **test phone numbers** (Authentication → Sign-in method → Phone) give fixed OTPs without SMS, which suits QA.

## Push notifications (Firebase Cloud Messaging)

Push uses the same Firebase files, so there's no extra app configuration.
- **Android:** works once `google-services.json` is in place.
- **iOS:** in the Firebase console, open Project settings → Cloud Messaging and upload an **APNs authentication key** (.p8, from Apple Developer → Keys) with your Team ID.
- **Addressing:** devices are addressed by **Firebase Installation ID**. The messaging plugin is configured with `installationIdEnabled: true`, and the app calls `register()` / `onRegistered`, not the deprecated `getToken()`.
- **Without Firebase:** development builds still run. There is no push, and the app falls back to faster polling.
- **Physical device needed:** push cannot be tested on the iOS Simulator. Android emulators need a Google Play system image.

To send real pushes from a local API, set `Firebase:UseDevelopmentVerifier=false` and configure a real service account, then raise and approve an SOS. With the development verifier on, the API logs pushes instead of sending them.

## Background location

Optional, off by default. People turn it on from the home card or Profile and privacy, after an in-app disclosure. Design, the API and store submission notes: [docs/06-background-location.md](../docs/06-background-location.md).
- **Needs a development build** (`expo-task-manager`, `expo-background-task`): Expo Go can't run background tasks.
- **Android:** grant "Allow all the time" on the settings page the app opens. To run the check-in now in a debug build, call `BackgroundTask.triggerTaskWorkerForTestingAsync()`. To see movement updates without walking, use the emulator's location controls (Extended controls → Location → Routes).
- **iOS:** a physical device is needed for the check-in (BGTaskScheduler isn't available in the Simulator). The Simulator's Features → Location → City Run exercises the movement updates and the geofence.
- **Battery savers:** some Android brands (Xiaomi, Oppo, Vivo, Samsung "sleeping apps") stop background work aggressively. Test on those phones, and if needed tell people to exclude the app from battery optimisation.

## Admin SOS map (Google Maps)

1. In Google Cloud (the same project as Firebase is fine), enable **Maps SDK for Android** and **Maps SDK for iOS**.
2. Create two API keys:
   - Android: restricted to *Android apps*, with the package name and SHA-1 fingerprint of each build;
   - iOS: restricted to *iOS apps* and the bundle id.
3. Set `GOOGLE_MAPS_ANDROID_API_KEY` and `GOOGLE_MAPS_IOS_API_KEY` in `.env.local`, or as EAS environment variables. Then rebuild the dev client: native configuration changes need a new binary.

Without the keys, development builds show a notice on the map screen, because the Android Maps SDK crashes without a key. Preview and production builds refuse to build without them. Map keys are embedded in the app, as with every mobile Maps app; the restrictions are what protect them.

## Checks

```powershell
npm run check       # tsc + eslint + jest
npx expo-doctor     # dependency/config health
```

`expo-doctor` reports Expo patch versions newer than the ones installed. That is deliberate: dependencies were installed with `npm_config_before=<date two weeks ago>` (project rule: only versions at least two weeks old). Upgrade the same way:

```powershell
$env:npm_config_before = (Get-Date).AddDays(-14).ToString('yyyy-MM-dd')
$env:EXPO_OFFLINE = '1'   # use the compatibility list of the installed expo package
npx expo install --fix
```

## Builds

| Profile | `APP_VARIANT` | App id | Auth |
|---|---|---|---|
| development | development | `com.communitysos.app.dev` | dev or Firebase |
| preview | preview | `com.communitysos.app.preview` | Firebase |
| production | production | `com.communitysos.app` | Firebase |

`npx eas-cli build --profile preview --platform android`. Before the first store build:
- replace the placeholders: the bundle id in `app.config.ts`, the API URLs and `INVITE_LINK_HOST` in `eas.json`, and the App Store id in `src/app/update-required.tsx`;
- add the Firebase files and the Maps keys as EAS environment variables.

## Layout

```
src/
  app/            routes (Expo Router); _layout.tsx holds all access guards
    (auth)/       welcome, phone, otp, join
    (app)/        signed-in area: home, sos/new, sos/[id], sos (history), alerts (nearby), location-background, profile, profile/edit
      admin/      admins only: hub + dashboard, queue, sos, map, invitations, users, referrals
    i/[code].tsx  invite deep link
    policy.tsx, profile-setup.tsx, update-required.tsx
  api/            HTTP client, problem+json errors, endpoint wrappers, wire types
  auth/           token manager (refresh rotation), session lifecycle, phone auth
  features/       feature hooks and components (app info, emergency numbers, sos, alerts, push, location, profile, admin)
index.ts          entry: registers the FCM background handler and the location tasks, then Expo Router
  i18n/           strings (type-checked keys)
  lib/            device info, secure storage, phone/invite/version helpers
  ui/             theme and accessible base components
```
