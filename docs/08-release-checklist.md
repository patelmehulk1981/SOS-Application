# Release checklist

Work through this before the first store release, and again (the ⟳ items) for every release.

## 1. Accounts and identifiers (once)

- [ ] Final bundle id chosen: `baseBundleId` in `mobile/app.config.ts`. It **cannot change** after the first upload.
- [ ] Google Play Console developer account (organisation, D-U-N-S verified), and an Apple Developer Program membership (organisation).
- [ ] Firebase project per environment (staging, prod), each with:
  - [ ] Android apps (`.preview` / production ids) with the debug, upload and **Play app-signing** SHA-256 fingerprints;
  - [ ] iOS apps with the same bundle ids;
  - [ ] Phone sign-in enabled, and SMS region policy restricted to India;
  - [ ] an **APNs authentication key** (.p8) with the Team ID uploaded under Cloud Messaging;
  - [ ] App Check considered (Play Integrity / App Attest) to protect SMS quota.
- [ ] Google Cloud keys:
  - [ ] Maps SDK for Android (restricted to package + SHA-1) and for iOS (bundle id);
  - [ ] a server Geocoding key (API-restricted) in Key Vault.
- [ ] Domain: the API at `api.<domain>` and invite links at `<domain>`:
  - [ ] `assetlinks.json` contains the Play app-signing SHA-256;
  - [ ] `apple-app-site-association` contains `<TeamID>.<bundleId>`;
  - [ ] both are served as `application/json` without redirects.
- [ ] EAS:
  - [ ] project linked;
  - [ ] file variables `GOOGLE_SERVICES_JSON` and `GOOGLE_SERVICE_INFO_PLIST` per environment;
  - [ ] secrets `GOOGLE_MAPS_ANDROID_API_KEY` and `GOOGLE_MAPS_IOS_API_KEY`;
  - [ ] `EXPO_TOKEN` in GitHub.
- [ ] Placeholders replaced: `eas.json` API URLs and `INVITE_LINK_HOST`; the App Store id in `src/app/update-required.tsx`; the `REPLACE_ME` values in `infra/bicep/*.bicepparam`.

## 2. Backend (⟳)

- [ ] CI is green, including the **SqlServer** category. Check the job log for "Passed" with no skipped SQL tests.
- [ ] The fan-out timings printed by `Five_km_search_and_dispatch_scale_to_ten_thousand_members` are within budget (selection well under 1 s on GP_Gen5_2).
- [ ] Migrations reviewed for expand-then-contract: nothing dropped or renamed that the running version still uses.
- [ ] Staging deployed and smoke-tested end to end with real phones: register by invite, raise an SOS, admin approves, a neighbour within 5 km gets the push, responds, and the closure notice arrives.
- [ ] Escalation tested: leave an SOS unreviewed for 3 minutes; the Master Admin gets the push and the alert email fires.
- [ ] `App:MinimumAppVersion` raised if this release breaks older apps.
- [ ] `App:PolicyVersion` bumped if the Privacy Notice or Terms changed. Users must re-accept.

## 3. Privacy and compliance (once, then on change)

- [ ] Privacy Notice and Terms published at the URLs in `EXPO_PUBLIC_PRIVACY_URL` / `EXPO_PUBLIC_TERMS_URL`. They cover:
  - [ ] phone number and profile;
  - [ ] optional health data;
  - [ ] location: foreground, background and last-known only;
  - [ ] photos (deleted 90 days after closure);
  - [ ] the purposes, the processors (Azure India, Google Firebase / Maps) and retention (docs/07);
  - [ ] rights: export, deletion and withdrawal; and the grievance officer.
- [ ] Grievance officer email set (`App:GrievanceContactEmail`, Bicep `grievanceContactEmail`).
- [ ] 18+ only stated in the store listings (age rating questionnaires).
- [ ] Data processing terms accepted with Microsoft and Google. Firebase data location reviewed.

## 4. Google Play (⟳ where marked)

- [ ] App content forms:
  - [ ] privacy policy URL;
  - [ ] ads: none;
  - [ ] app access: give reviewers an invite code and a **Firebase test phone number** with a fixed OTP;
  - [ ] content rating;
  - [ ] target audience: 18+;
  - [ ] news app: no;
  - [ ] health apps declaration (blood group is optional user-entered data).
- [ ] **Data safety:**
  - [ ] phone number;
  - [ ] name and email (optional);
  - [ ] precise and approximate location;
  - [ ] photos;
  - [ ] health info (optional);
  - [ ] device IDs (push installation id);
  - [ ] app interactions;
  - [ ] encrypted in transit; deletion available in the app;
  - [ ] not sold and not used for ads.
- [ ] **Location permissions declaration** (background location, see [06](06-background-location.md#store-submission)), with a video of the disclosure screen, the permission prompt and an alert arriving with the app closed.
- [ ] Foreground service: none declared (the app doesn't use one).
- [ ] Notifications: POST_NOTIFICATIONS is requested in context.
- [ ] ⟳ Upload to the **internal testing** track, then closed testing. New personal accounts must complete Google's 12-tester / 14-day closed test.
- [ ] ⟳ Pre-launch report reviewed: crashes, accessibility.

## 5. App Store (⟳ where marked)

- [ ] App Privacy ("nutrition label"), matching the Play Data safety answers. No tracking (no ATT prompt needed).
- [ ] Review notes:
  - [ ] invite code and test phone number / OTP;
  - [ ] the community alert purpose;
  - [ ] background location is optional and off by default (guidelines 5.1.1, 5.1.5);
  - [ ] push is used for alerts.
- [ ] Purpose strings reviewed: location when in use, location always, camera, photos.
- [ ] Sign in with Apple: not required (phone-number sign-in only, no third-party login).
- [ ] ⟳ TestFlight internal, then external testers, before release.

## 6. Every release (⟳)

- [ ] Version bumped in `app.config.ts` (`version`). EAS auto-increments build numbers.
- [ ] `npm run check` and `npx expo-doctor` reviewed. Patch versions held back on purpose by the two-week rule are fine.
- [ ] Release notes written, with no personal data in screenshots.
- [ ] `mobile-release` workflow run: `preview` for testers, then `production` with submit.
- [ ] Staged rollout on Play (10% → 50% → 100%) while watching crash rate and the API's 5xx and push-failure alerts.
- [ ] After release: App Insights checked for new exceptions, the escalation alert count, and FCM failure rate.

## 7. Operations readiness (once)

- [ ] On-call rota for the alert emails; the escalation alert means SOS are waiting for review.
- [ ] Each Sub-Admin has the app installed with notifications on (reviews time out to the Master Admin after 3 minutes).
- [ ] Backup restore rehearsed once (point-in-time restore to a new database).
- [ ] Incident process for personal-data breaches: DPDP requires notifying the Data Protection Board and affected people; CERT-In requires a report within 6 hours.
