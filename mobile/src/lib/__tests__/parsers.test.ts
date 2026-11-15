import { formatDistance } from '../format';
import { extractInviteCode, isWellFormedInviteCode } from '../inviteCode';
import { formatPhone, maskPhone, toE164 } from '../phone';
import { compareVersions, isBelowMinimum } from '../version';

describe('toE164 (mirrors backend PhoneInput)', () => {
  it.each([
    ['9876543210', '+919876543210'],
    ['98765 43210', '+919876543210'],
    ['098765-43210', '+919876543210'],
    ['919876543210', '+919876543210'],
    ['+91 98765 43210', '+919876543210'],
    ['+14155552671', '+14155552671'],
  ])('%s → %s', (input, expected) => {
    expect(toE164(input)).toBe(expected);
  });

  it.each(['', '12345', '5876543210', '+0123456789', '+91 98765', 'abc'])('rejects %p', (input) => {
    expect(toE164(input)).toBeNull();
  });

  it('formats and masks Indian numbers', () => {
    expect(formatPhone('+919876543210')).toBe('+91 98765 43210');
    expect(maskPhone('+919876543210')).toBe('+91 ••••• •3210');
  });
});

describe('invite codes (mirrors backend InvitationCode)', () => {
  it('accepts 10 characters from the unambiguous alphabet, case-insensitively', () => {
    expect(isWellFormedInviteCode('K7QX2M9PRA')).toBe(true);
    expect(isWellFormedInviteCode(' k7qx2m9pra ')).toBe(true);
    expect(isWellFormedInviteCode('K7QX2M9PR0')).toBe(false); // 0 is excluded
    expect(isWellFormedInviteCode('K7QX2M9PRI')).toBe(false); // I is excluded
    expect(isWellFormedInviteCode('K7QX2M9PRL')).toBe(false); // L is excluded
    expect(isWellFormedInviteCode('K7QX2M9PR')).toBe(false);
  });

  it('extracts the code from a pasted link or message', () => {
    expect(extractInviteCode('https://sos.example.com/i/K7QX2M9PRA')).toBe('K7QX2M9PRA');
    expect(extractInviteCode('Join our SOS group: https://sos.example.com/i/k7qx2m9pra thanks')).toBe('K7QX2M9PRA');
    expect(extractInviteCode('K7QX2 M9PRA')).toBe('K7QX2M9PRA');
    expect(extractInviteCode('https://sos.example.com/i/K7QX2M9PRAEXTRA')).toBeNull();
    expect(extractInviteCode('hello')).toBeNull();
  });
});

describe('version comparison', () => {
  it('compares numerically, not lexically', () => {
    expect(compareVersions('1.10.0', '1.9.3')).toBe(1);
    expect(compareVersions('1.0', '1.0.0')).toBe(0);
    expect(compareVersions('1.0.0-beta.1', '1.0.0')).toBe(0);
    expect(compareVersions('0.9.9', '1.0.0')).toBe(-1);
  });

  it('only blocks when a minimum is configured and not met', () => {
    expect(isBelowMinimum('1.0.0', '1.0.1')).toBe(true);
    expect(isBelowMinimum('1.0.1', '1.0.1')).toBe(false);
    expect(isBelowMinimum('1.0.0', '')).toBe(false);
    expect(isBelowMinimum('1.0.0', undefined)).toBe(false);
  });
});

describe('formatDistance', () => {
  it.each([
    [0, '50 m'],
    [120, '100 m'],
    [940, '950 m'],
    [960, '1.0 km'],
    [1_449, '1.4 km'],
    [4_960, '5.0 km'],
  ])('%d m → %s', (meters, expected) => {
    expect(formatDistance(meters)).toBe(expected);
  });

  it('returns nothing for impossible values', () => {
    expect(formatDistance(Number.NaN)).toBe('');
    expect(formatDistance(-5)).toBe('');
  });
});
