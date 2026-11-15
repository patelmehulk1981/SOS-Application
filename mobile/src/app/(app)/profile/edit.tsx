import { useRouter } from 'expo-router';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ActivityIndicator, Pressable, StyleSheet, Text, View } from 'react-native';

import type { Profile } from '@/api/types';
import { useProfile, useUpdateProfile } from '@/features/profile/queries';
import { toE164 } from '@/lib/phone';
import { Banner } from '@/ui/Banner';
import { Button } from '@/ui/Button';
import { describeError, type ErrorDescription } from '@/ui/describeError';
import { Screen } from '@/ui/Screen';
import { TextField } from '@/ui/TextField';
import { colors, radius, spacing, typography } from '@/ui/theme';

const BLOOD_GROUPS = ['A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-'] as const;
const MAX_NAME = 100;
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export default function EditProfileScreen() {
  const profile = useProfile();
  if (!profile.data) {
    return (
      <View style={styles.center}>
        <ActivityIndicator size="large" color={colors.primary} />
      </View>
    );
  }
  // Keyed so the form re-initialises if the profile is replaced underneath.
  return <EditProfileForm key={profile.data.id} profile={profile.data} />;
}

function EditProfileForm({ profile }: { profile: Profile }) {
  const { t } = useTranslation();
  const router = useRouter();
  const update = useUpdateProfile();
  const healthAllowed = profile.consents.healthData;

  const [fullName, setFullName] = useState(profile.fullName ?? '');
  const [email, setEmail] = useState(profile.email ?? '');
  const [bloodGroup, setBloodGroup] = useState<string | null>(profile.bloodGroup ?? null);
  const [contactName, setContactName] = useState(profile.emergencyContactName ?? '');
  const [contactPhone, setContactPhone] = useState(profile.emergencyContactPhone ?? '');
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [failure, setFailure] = useState<ErrorDescription | null>(null);

  const save = () => {
    const name = fullName.trim().replace(/\s+/g, ' ');
    const mail = email.trim();
    const phone = contactPhone.trim();
    const next: Record<string, string> = {};
    if (name === '') next.fullName = t('profileSetup.nameRequired');
    else if (name.length > MAX_NAME) next.fullName = t('profileSetup.nameTooLong');
    if (mail !== '' && !EMAIL.test(mail)) next.email = t('profileEdit.emailInvalid');
    if (healthAllowed && phone !== '' && !toE164(phone)) next.contactPhone = t('profileEdit.phoneInvalid');
    setErrors(next);
    if (Object.keys(next).length > 0) return;

    setFailure(null);
    // PUT /me/profile replaces the whole profile: always send every field we want to keep.
    update.mutate(
      {
        fullName: name,
        email: mail || undefined,
        ...(healthAllowed
          ? {
              bloodGroup: bloodGroup ?? undefined,
              emergencyContactName: contactName.trim() || undefined,
              emergencyContactPhone: phone ? (toE164(phone) ?? phone) : undefined,
            }
          : {}),
      },
      {
        onSuccess: () => router.back(),
        onError: (e) => setFailure(describeError(e, t)),
      },
    );
  };

  return (
    <Screen footer={<Button title={t('profileEdit.save')} onPress={save} loading={update.isPending} />}>
      <TextField
        label={t('profileEdit.fullName')}
        value={fullName}
        onChangeText={setFullName}
        autoCapitalize="words"
        autoComplete="name"
        maxLength={MAX_NAME + 20}
        error={errors.fullName}
      />
      <TextField
        label={t('profileEdit.email')}
        value={email}
        onChangeText={setEmail}
        keyboardType="email-address"
        autoCapitalize="none"
        autoComplete="email"
        maxLength={256}
        error={errors.email}
      />

      <View style={styles.group}>
        <Text style={typography.heading}>{t('profileEdit.healthTitle')}</Text>
        {!healthAllowed ? <Banner tone="info" message={t('profileEdit.healthConsentNeeded')} /> : null}
        <Text style={typography.label}>{t('profileEdit.bloodGroup')}</Text>
        <View style={styles.chips} accessibilityRole="radiogroup">
          {[null, ...BLOOD_GROUPS].map((group) => {
            const selected = bloodGroup === group;
            return (
              <Pressable
                key={group ?? 'none'}
                accessibilityRole="radio"
                accessibilityState={{ selected, disabled: !healthAllowed }}
                disabled={!healthAllowed}
                onPress={() => setBloodGroup(group)}
                style={[styles.chip, selected && styles.chipSelected, !healthAllowed && styles.chipDisabled]}
              >
                <Text style={[styles.chipText, selected && styles.chipTextSelected]}>{group ?? t('profileEdit.bloodGroupNone')}</Text>
              </Pressable>
            );
          })}
        </View>
        <TextField
          label={t('profileEdit.emergencyName')}
          value={contactName}
          onChangeText={setContactName}
          editable={healthAllowed}
          autoCapitalize="words"
          maxLength={MAX_NAME}
        />
        <TextField
          label={t('profileEdit.emergencyPhone')}
          value={contactPhone}
          onChangeText={setContactPhone}
          editable={healthAllowed}
          keyboardType="phone-pad"
          maxLength={18}
          error={errors.contactPhone}
        />
      </View>

      {failure ? <Banner tone="error" message={failure.message} detail={failure.reference} /> : null}
    </Screen>
  );
}

const styles = StyleSheet.create({
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: colors.background },
  group: { gap: spacing.md },
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  chip: {
    minWidth: 56,
    minHeight: 44,
    paddingHorizontal: spacing.md,
    borderRadius: radius.pill,
    borderWidth: 1.5,
    borderColor: colors.border,
    alignItems: 'center',
    justifyContent: 'center',
  },
  chipSelected: { borderColor: colors.primary, backgroundColor: colors.dangerSurface },
  chipDisabled: { opacity: 0.5 },
  chipText: { fontSize: 16, color: colors.text, fontWeight: '600' },
  chipTextSelected: { color: colors.primary },
});
