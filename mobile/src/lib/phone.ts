/**
 * Mirrors the API's PhoneInput rules (backend Application/Common/PhoneInput.cs):
 * a 10-digit Indian mobile (6–9 first digit, optional leading 0) becomes +91…; anything starting
 * with + must already be E.164.
 */
const E164 = /^\+[1-9]\d{7,14}$/;

export function toE164(raw: string): string | null {
  const trimmed = raw.trim();
  if (trimmed === '') return null;

  if (trimmed.startsWith('+')) {
    const candidate = `+${trimmed.slice(1).replace(/[\s\-().]/g, '')}`;
    return E164.test(candidate) ? candidate : null;
  }

  let digits = trimmed.replace(/\D/g, '');
  if (digits.length === 11 && digits.startsWith('0')) digits = digits.slice(1);
  if (digits.length === 12 && digits.startsWith('91')) digits = digits.slice(2);
  if (digits.length === 10 && /^[6-9]/.test(digits)) return `+91${digits}`;
  return null;
}

/** "+919876543210" → "+91 98765 43210" for display. Other countries are shown as stored. */
export function formatPhone(e164: string): string {
  const match = /^\+91(\d{5})(\d{5})$/.exec(e164);
  return match ? `+91 ${match[1]} ${match[2]}` : e164;
}

/** "+919876543210" → "+91 ••••• •3210" for places where the full number is not needed. */
export function maskPhone(e164: string): string {
  if (e164.length < 6) return e164;
  return `${e164.slice(0, 3)} ••••• •${e164.slice(-4)}`;
}
