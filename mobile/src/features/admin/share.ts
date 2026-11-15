import type { TFunction } from 'i18next';
import { Share } from 'react-native';

import type { Invitation } from '@/api/types';

/**
 * Opens the system share sheet (WhatsApp, SMS, copy…) with the invite link. The message carries the
 * link, so it works in apps that drop the separate `url` field. Dismissing the sheet is not an error.
 */
export async function shareInvitation(invitation: Pick<Invitation, 'link'>, t: TFunction): Promise<void> {
  await Share.share({ message: t('admin.invitations.shareMessage', { link: invitation.link }) });
}
