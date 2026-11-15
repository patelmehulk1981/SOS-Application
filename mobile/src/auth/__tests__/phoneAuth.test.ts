import { createDevPhoneAuth, PhoneAuthError } from '../phoneAuth';

describe('development phone auth', () => {
  it('issues the API dev token only after the right code', async () => {
    const auth = createDevPhoneAuth('123456');
    await expect(auth.getIdToken()).rejects.toBeInstanceOf(PhoneAuthError);

    await auth.sendCode('+919876543210');
    await expect(auth.confirmCode('000000')).rejects.toMatchObject({ kind: 'invalidCode' });
    await expect(auth.getIdToken()).rejects.toMatchObject({ kind: 'notStarted' });

    await auth.confirmCode('123456');
    await expect(auth.getIdToken()).resolves.toBe('dev:+919876543210');

    await auth.signOut();
    await expect(auth.getIdToken()).rejects.toBeInstanceOf(PhoneAuthError);
  });

  it('requires a code to have been requested', async () => {
    const auth = createDevPhoneAuth('123456');
    await expect(auth.confirmCode('123456')).rejects.toMatchObject({ kind: 'notStarted' });
  });
});
