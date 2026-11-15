import { Redirect, useRouter } from 'expo-router';
import { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Text } from 'react-native';

import { isApiError } from '@/api/errors';
import { phoneAuth } from '@/auth/phoneAuth';
import { loginWithVerifiedPhone } from '@/auth/session';
import { useSignupStore } from '@/auth/signupStore';
import { formatPhone } from '@/lib/phone';
import { Banner } from '@/ui/Banner';
import { Button } from '@/ui/Button';
import { describeError, type ErrorDescription } from '@/ui/describeError';
import { EmergencyCallBar } from '@/ui/EmergencyCallBar';
import { Screen } from '@/ui/Screen';
import { TextField } from '@/ui/TextField';
import { typography } from '@/ui/theme';

const RESEND_AFTER_SECONDS = 30;
const OTP_PATTERN = /^\d{6}$/;

export default function OtpScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const phone = useSignupStore((s) => s.phone);
  const [code, setCode] = useState('');
  const [fieldError, setFieldError] = useState<string | null>(null);
  const [error, setError] = useState<ErrorDescription | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [resendIn, setResendIn] = useState(RESEND_AFTER_SECONDS);
  const inFlight = useRef(false);

  useEffect(() => {
    if (resendIn <= 0) return;
    const timer = setTimeout(() => setResendIn((s) => s - 1), 1000);
    return () => clearTimeout(timer);
  }, [resendIn]);

  /** Phone verified with Firebase: exchange it for an API session. */
  const finishSignIn = useCallback(async () => {
    try {
      await loginWithVerifiedPhone();
      // Signed in: the root guard leaves the (auth) stack automatically.
    } catch (e) {
      if (isApiError(e, 'auth.registration_required')) {
        router.replace('/join');
        return;
      }
      throw e;
    }
  }, [router]);

  const run = useCallback(
    async (work: () => Promise<void>) => {
      if (inFlight.current) return;
      inFlight.current = true;
      setBusy(true);
      setError(null);
      try {
        await work();
      } catch (e) {
        setError(describeError(e, t));
      } finally {
        inFlight.current = false;
        setBusy(false);
      }
    },
    [t],
  );

  // Android may verify the number without the user typing the code.
  useEffect(
    () =>
      phoneAuth.onAutoVerified(() => {
        setInfo(t('otp.autoVerified'));
        void run(finishSignIn);
      }),
    [finishSignIn, run, t],
  );

  if (!phone) {
    // Opened without a number (e.g. state lost after a reload): start again.
    return <Redirect href="/phone" />;
  }

  const verify = () => {
    if (!OTP_PATTERN.test(code)) {
      setFieldError(t('otp.invalid'));
      return;
    }
    setFieldError(null);
    void run(async () => {
      await phoneAuth.confirmCode(code);
      await finishSignIn();
    });
  };

  const resend = () =>
    void run(async () => {
      await phoneAuth.sendCode(phone);
      setCode('');
      setResendIn(RESEND_AFTER_SECONDS);
    });

  return (
    <Screen
      footer={
        <>
          <Button title={t('otp.verify')} onPress={verify} loading={busy} />
          <EmergencyCallBar />
        </>
      }
    >
      <Text style={typography.title} accessibilityRole="header">
        {t('otp.title')}
      </Text>
      <Text style={typography.body}>{t('otp.sentTo', { phone: formatPhone(phone) })}</Text>
      <TextField
        label={t('otp.label')}
        value={code}
        onChangeText={(text) => {
          const digits = text.replace(/\D/g, '').slice(0, 6);
          setCode(digits);
          if (fieldError) setFieldError(null);
        }}
        keyboardType="number-pad"
        textContentType="oneTimeCode"
        autoComplete="sms-otp"
        maxLength={6}
        autoFocus
        onSubmitEditing={verify}
        error={fieldError}
        style={{ letterSpacing: 8, fontSize: 24 }}
      />
      {info ? <Banner tone="success" message={info} /> : null}
      {error ? <Banner tone="error" message={error.message} detail={error.reference} /> : null}
      <Button
        variant="ghost"
        title={resendIn > 0 ? t('otp.resendIn', { seconds: resendIn }) : t('otp.resend')}
        disabled={resendIn > 0 || busy}
        onPress={resend}
      />
      <Button variant="ghost" title={t('otp.changeNumber')} onPress={() => router.back()} disabled={busy} />
    </Screen>
  );
}
