import { Redirect, useLocalSearchParams } from 'expo-router';
import { useEffect } from 'react';

import { useSessionStore } from '@/auth/sessionStore';
import { useSignupStore } from '@/auth/signupStore';
import { isWellFormedInviteCode, normalizeInviteCode } from '@/lib/inviteCode';

/**
 * Invite link target: https://<invite host>/i/{code} (App Link / Universal Link) or communitysos://i/{code}.
 * Remembers the code for sign-up and continues to the normal flow. Existing members are simply
 * taken home: their referrer never changes (docs/03b-auth-profile.md).
 */
export default function InviteLinkRoute() {
  const { code } = useLocalSearchParams<{ code?: string }>();
  const status = useSessionStore((s) => s.status);
  const setPendingInviteCode = useSignupStore((s) => s.setPendingInviteCode);

  const normalized = typeof code === 'string' ? normalizeInviteCode(code) : '';
  const valid = isWellFormedInviteCode(normalized);

  useEffect(() => {
    if (valid && status !== 'signedIn') setPendingInviteCode(normalized);
  }, [normalized, setPendingInviteCode, status, valid]);

  return <Redirect href={status === 'signedIn' ? '/' : '/welcome'} />;
}
