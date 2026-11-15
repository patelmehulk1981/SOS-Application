import { useRouter } from 'expo-router';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Text } from 'react-native';

import { phoneAuth } from '@/auth/phoneAuth';
import { useSignupStore } from '@/auth/signupStore';
import { env } from '@/config/env';
import { toE164 } from '@/lib/phone';
import { Banner } from '@/ui/Banner';
import { Button } from '@/ui/Button';
import { describeError, type ErrorDescription } from '@/ui/describeError';
import { EmergencyCallBar } from '@/ui/EmergencyCallBar';
import { Screen } from '@/ui/Screen';
import { TextField } from '@/ui/TextField';
import { typography } from '@/ui/theme';

export default function PhoneScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const setPhone = useSignupStore((s) => s.setPhone);
  const [input, setInput] = useState('');
  const [fieldError, setFieldError] = useState<string | null>(null);
  const [error, setError] = useState<ErrorDescription | null>(null);
  const [sending, setSending] = useState(false);

  const submit = async () => {
    // "+…" is taken as typed; otherwise the visible +91 prefix applies.
    const phone = toE164(input);
    if (!phone) {
      setFieldError(t('phone.invalid'));
      return;
    }
    setFieldError(null);
    setError(null);
    setSending(true);
    try {
      await phoneAuth.sendCode(phone);
      setPhone(phone);
      router.push('/otp');
    } catch (e) {
      setError(describeError(e, t));
    } finally {
      setSending(false);
    }
  };

  return (
    <Screen
      footer={
        <>
          <Button title={t('common.continue')} onPress={submit} loading={sending} />
          <EmergencyCallBar />
        </>
      }
    >
      <Text style={typography.title} accessibilityRole="header">
        {t('phone.title')}
      </Text>
      <TextField
        label={t('phone.label')}
        prefix={input.trim().startsWith('+') ? undefined : '+91'}
        placeholder={t('phone.placeholder')}
        value={input}
        onChangeText={(text) => {
          setInput(text);
          if (fieldError) setFieldError(null);
        }}
        keyboardType="phone-pad"
        textContentType="telephoneNumber"
        autoComplete="tel"
        maxLength={18}
        autoFocus
        returnKeyType="done"
        onSubmitEditing={submit}
        hint={t('phone.hint')}
        error={fieldError}
      />
      {env.authMode === 'dev' ? <Banner tone="info" message={t('phone.devHint', { code: env.devOtp })} /> : null}
      {error ? <Banner tone="error" message={error.message} detail={error.reference} /> : null}
    </Screen>
  );
}
