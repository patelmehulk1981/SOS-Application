/**
 * This phone's current Firebase Installation ID once registered for push. Kept outside the push feature
 * so sign-out (src/auth) can tell the API to stop pushing to this phone without importing feature code.
 */
let current: string | null = null;

export const pushInstallation = {
  get: (): string | null => current,
  set: (installationId: string | null): void => {
    current = installationId;
  },
};
