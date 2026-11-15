import { useTranslation } from 'react-i18next';

import type { UserStatus } from '@/api/types';
import { Pill, type PillTone } from '@/ui/Pill';

const TONES: Record<UserStatus, PillTone> = { Active: 'success', Suspended: 'danger', Deleted: 'neutral' };

export function UserStatusPill({ status }: { status: UserStatus }) {
  const { t } = useTranslation();
  return <Pill label={t(`admin.users.userStatus.${status}`)} tone={TONES[status]} />;
}
