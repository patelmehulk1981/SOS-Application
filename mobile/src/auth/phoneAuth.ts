import { env } from '@/config/env';

/**
 * Phone-number ownership proof (OTP). The API only ever sees the resulting Firebase ID token
 * (docs/03b-auth-profile.md); after the API login succeeds the Firebase session is signed out,
 * because the app runs on the API's own tokens.
 */
export interface PhoneAuthProvider {
  /** Sends the SMS code. On Android the code may be read automatically (see onAutoVerified). */
  sendCode(phoneE164: string): Promise<void>;
  /** Confirms the code the user typed. */
  confirmCode(code: string): Promise<void>;
  /** Android instant verification: Firebase verified the number without the user typing the code. */
  onAutoVerified(listener: () => void): () => void;
  /** Fresh ID token for the verified number. */
  getIdToken(): Promise<string>;
  signOut(): Promise<void>;
}

export type PhoneAuthErrorKind =
  | 'invalidPhone'
  | 'invalidCode'
  | 'codeExpired'
  | 'tooManyAttempts'
  | 'network'
  | 'notStarted'
  | 'unavailable'
  | 'unknown';

export class PhoneAuthError extends Error {
  constructor(
    readonly kind: PhoneAuthErrorKind,
    message: string,
    options?: { cause?: unknown },
  ) {
    super(message, options);
    this.name = 'PhoneAuthError';
  }
}

// ---------------------------------------------------------------------------------------------
// Firebase Phone Auth (React Native Firebase, modular API). Loaded lazily so a JS-only dev setup
// without the native Firebase module (or Expo Go with EXPO_PUBLIC_AUTH_MODE=dev) still starts.
// ---------------------------------------------------------------------------------------------

type FirebaseAuthModule = typeof import('@react-native-firebase/auth');
type ConfirmationResult = Awaited<ReturnType<FirebaseAuthModule['signInWithPhoneNumber']>>;

function loadFirebaseAuth() {
  try {
    // eslint-disable-next-line @typescript-eslint/no-require-imports
    const mod = require('@react-native-firebase/auth') as FirebaseAuthModule;
    return { mod, auth: mod.getAuth() };
  } catch (cause) {
    throw new PhoneAuthError(
      'unavailable',
      'Firebase is not included in this build. Rebuild the development client with the Firebase config files, or use EXPO_PUBLIC_AUTH_MODE=dev.',
      { cause },
    );
  }
}

export function createFirebasePhoneAuth(): PhoneAuthProvider {
  let confirmation: ConfirmationResult | null = null;
  let pendingPhone: string | null = null;

  const firebase = loadFirebaseAuth;

  return {
    async sendCode(phoneE164) {
      const { mod, auth } = firebase();
      try {
        // Start clean so a leftover Firebase user is not mistaken for an instant verification.
        if (auth.currentUser) await mod.signOut(auth);
        confirmation = await mod.signInWithPhoneNumber(auth, phoneE164);
        pendingPhone = phoneE164;
      } catch (error) {
        throw toPhoneAuthError(error);
      }
    },

    async confirmCode(code) {
      if (!confirmation) throw new PhoneAuthError('notStarted', 'Request a new code first.');
      try {
        await confirmation.confirm(code);
      } catch (error) {
        throw toPhoneAuthError(error);
      }
    },

    onAutoVerified(listener) {
      const { mod, auth } = firebase();
      return mod.onAuthStateChanged(auth, (user) => {
        if (user && pendingPhone && user.phoneNumber === pendingPhone) listener();
      });
    },

    async getIdToken() {
      const { mod, auth } = firebase();
      const user = auth.currentUser;
      if (!user) throw new PhoneAuthError('notStarted', 'Verify your phone number first.');
      try {
        return await mod.getIdToken(user, true);
      } catch (error) {
        throw toPhoneAuthError(error);
      }
    },

    async signOut() {
      confirmation = null;
      pendingPhone = null;
      try {
        const { mod, auth } = firebase();
        if (auth.currentUser) await mod.signOut(auth);
      } catch {
        // Best effort: the app session does not depend on Firebase's.
      }
    },
  };
}

function toPhoneAuthError(error: unknown): PhoneAuthError {
  if (error instanceof PhoneAuthError) return error;
  const code = typeof error === 'object' && error !== null && 'code' in error ? String(error.code) : '';
  switch (code) {
    case 'auth/invalid-phone-number':
    case 'auth/missing-phone-number':
      return new PhoneAuthError('invalidPhone', 'That phone number is not valid.', { cause: error });
    case 'auth/invalid-verification-code':
    case 'auth/missing-verification-code':
      return new PhoneAuthError('invalidCode', 'The code is incorrect.', { cause: error });
    case 'auth/session-expired':
    case 'auth/code-expired':
      return new PhoneAuthError('codeExpired', 'The code has expired. Request a new one.', { cause: error });
    case 'auth/too-many-requests':
    case 'auth/quota-exceeded':
      return new PhoneAuthError('tooManyAttempts', 'Too many attempts. Try again later.', { cause: error });
    case 'auth/network-request-failed':
      return new PhoneAuthError('network', 'No internet connection.', { cause: error });
    default:
      return new PhoneAuthError('unknown', 'Phone verification failed. Try again.', { cause: error });
  }
}

// ---------------------------------------------------------------------------------------------
// Development bypass: no SMS. Any number; the code is env.devOtp. The API accepts "dev:<phone>"
// only in its Development environment (Firebase:UseDevelopmentVerifier).
// ---------------------------------------------------------------------------------------------

export function createDevPhoneAuth(validCode: string = env.devOtp): PhoneAuthProvider {
  let phone: string | null = null;
  let verified = false;

  return {
    async sendCode(phoneE164) {
      phone = phoneE164;
      verified = false;
    },
    async confirmCode(code) {
      if (!phone) throw new PhoneAuthError('notStarted', 'Request a new code first.');
      if (code !== validCode) throw new PhoneAuthError('invalidCode', 'The code is incorrect.');
      verified = true;
    },
    onAutoVerified() {
      return () => {};
    },
    async getIdToken() {
      if (!phone || !verified) throw new PhoneAuthError('notStarted', 'Verify your phone number first.');
      return `dev:${phone}`;
    },
    async signOut() {
      phone = null;
      verified = false;
    },
  };
}

export const phoneAuth: PhoneAuthProvider = env.authMode === 'dev' ? createDevPhoneAuth() : createFirebasePhoneAuth();
