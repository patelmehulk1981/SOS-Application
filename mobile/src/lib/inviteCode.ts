/**
 * Invite codes (backend Domain/ValueObjects/InvitationCode.cs): 10 characters from an alphabet
 * without look-alikes (no 0/O, 1/I/L). Case-insensitive.
 */
export const INVITE_CODE_LENGTH = 10;
const ALPHABET = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789';
const CODE_PATTERN = new RegExp(`^[${ALPHABET}]{${INVITE_CODE_LENGTH}}$`);

export function normalizeInviteCode(raw: string): string {
  return raw.trim().toUpperCase();
}

export function isWellFormedInviteCode(raw: string): boolean {
  return CODE_PATTERN.test(normalizeInviteCode(raw));
}

/**
 * Accepts what people actually paste: the bare code, the full link
 * (https://host/i/K7QX2M9PRA), or a message containing the link.
 */
export function extractInviteCode(text: string): string | null {
  const fromLink = /\/i\/([A-Za-z0-9]{10})(?![A-Za-z0-9])/.exec(text);
  if (fromLink?.[1] && isWellFormedInviteCode(fromLink[1])) return normalizeInviteCode(fromLink[1]);

  const compact = text.replace(/[\s-]/g, '');
  if (isWellFormedInviteCode(compact)) return normalizeInviteCode(compact);
  return null;
}
