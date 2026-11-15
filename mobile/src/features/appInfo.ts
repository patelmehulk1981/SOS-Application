import { useQuery } from '@tanstack/react-query';

import { appInfoApi } from '@/api/endpoints';
import type { AppInfo, EmergencyNumber } from '@/api/types';
import { appVersion, platform } from '@/lib/device';
import { isBelowMinimum } from '@/lib/version';

export const appInfoQueryKey = ['app-info'] as const;

/** Used until /app-info has loaded (or when offline): emergency numbers must always be available. */
export const FALLBACK_EMERGENCY_NUMBERS: EmergencyNumber[] = [
  { label: 'National Emergency', number: '112' },
  { label: 'Ambulance', number: '108' },
  { label: 'Fire', number: '101' },
  { label: 'Police', number: '100' },
  { label: 'Women Helpline', number: '1091' },
];

export function useAppInfo() {
  return useQuery({
    queryKey: appInfoQueryKey,
    queryFn: appInfoApi.get,
    staleTime: 10 * 60_000,
  });
}

export function isUpdateRequired(info: AppInfo | undefined): boolean {
  if (!info) return false; // never block the app because the check could not run
  const minimum = platform === 'Ios' ? info.minimumAppVersion.ios : info.minimumAppVersion.android;
  return isBelowMinimum(appVersion, minimum);
}
