import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Text } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { meApi } from '@/api/endpoints';
import { updateUser } from '@/auth/session';
import { Banner } from '@/ui/Banner';
import { Button } from '@/ui/Button';
import { describeError, type ErrorDescription } from '@/ui/describeError';
import { EmergencyCallBar } from '@/ui/EmergencyCallBar';
import { Screen } from '@/ui/Screen';
import { TextField } from '@/ui/TextField';
import { colors, typography } from '@/ui/theme';

const MAX_NAME_LENGTH = 100; // backend User.FullNameMaxLength

/** New members set their name (SessionUser.profileCompleted = false). Health details come later, with consent. */
export default function ProfileSetupScreen() {
  const { t } = useTranslation();
  const [name, setName] = useState('');
  const [fieldError, setFieldError] = useState<string | null>(null);
  const [error, setError] = useState<ErrorDescription | null>(null);
  const [saving, setSaving] = useState(false);

  const save = async () => {
    const fullName = name.trim().replace(/\s+/g, ' ');
    if (fullName === '') return setFieldError(t('profileSetup.nameRequired'));
    if (fullName.length > MAX_NAME_LENGTH) return setFieldError(t('profileSetup.nameTooLong'));
    setSaving(true);
    setError(null);
    try {
      // Profile is still empty at this point, so the PUT (full replacement) loses nothing.
      const profile = await meApi.updateProfile({ fullName });
      await updateUser((user) => ({ ...user, fullName: profile.fullName ?? fullName, profileCompleted: true }));
    } catch (e) {
      setError(describeError(e, t));
    } finally {
      setSaving(false);
    }
  };

  return (
    <SafeAreaView style={{ flex: 1, backgroundColor: colors.background }} edges={['top']}>
      <Screen
        footer={
          <>
            <Button title={t('profileSetup.save')} onPress={save} loading={saving} />
            <EmergencyCallBar />
          </>
        }
      >
        <Text style={typography.title} accessibilityRole="header">
          {t('profileSetup.title')}
        </Text>
        <Text style={typography.body}>{t('profileSetup.body')}</Text>
        <TextField
          label={t('profileSetup.nameLabel')}
          value={name}
          onChangeText={(text) => {
            setName(text);
            if (fieldError) setFieldError(null);
          }}
          autoCapitalize="words"
          autoComplete="name"
          textContentType="name"
          maxLength={MAX_NAME_LENGTH + 20}
          autoFocus
          returnKeyType="done"
          onSubmitEditing={save}
          error={fieldError}
        />
        {error ? <Banner tone="error" message={error.message} detail={error.reference} /> : null}
      </Screen>
    </SafeAreaView>
  );
}
