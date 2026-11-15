import type { TFunction } from 'i18next';

import { ApiError, NetworkError, SessionExpiredError } from '@/api/errors';
import { PhoneAuthError } from '@/auth/phoneAuth';

export interface ErrorDescription {
  message: string;
  /** Correlation id to quote to support (docs/03a: "quote correlationId in support requests"). */
  reference?: string;
}

/**
 * User-facing text for any error. Screens handle the codes they can act on themselves
 * (e.g. auth.registration_required) and fall back to this for everything else.
 * The API's `detail` is written for end users, so it is preferred over generic text.
 */
export function describeError(error: unknown, t: TFunction): ErrorDescription {
  if (error instanceof PhoneAuthError) {
    return { message: t(`errors.phoneAuth.${error.kind}`) };
  }
  if (error instanceof NetworkError) {
    return { message: t(error.timedOut ? 'errors.timeout' : 'errors.network') };
  }
  if (error instanceof SessionExpiredError) {
    return { message: t('errors.sessionExpired') };
  }
  if (error instanceof ApiError) {
    const reference = error.correlationId ? t('errors.supportReference', { id: error.correlationId }) : undefined;
    if (error.status === 429) return { message: t('errors.rateLimited'), reference };
    if (error.status >= 500) return { message: t('errors.server'), reference };
    const firstFieldError = Object.values(error.fieldErrors)[0]?.[0];
    if (error.code === 'validation_failed' && firstFieldError) return { message: firstFieldError, reference };
    return { message: error.detail ?? firstFieldError ?? t('errors.unknown'), reference };
  }
  return { message: t('errors.unknown') };
}
