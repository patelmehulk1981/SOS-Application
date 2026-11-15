import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { File, Paths } from 'expo-file-system';
import * as Sharing from 'expo-sharing';

import { meApi } from '@/api/endpoints';
import type { ConsentState, Profile, UpdateProfileRequest } from '@/api/types';
import { updateUser } from '@/auth/session';

export const profileKey = ['me'] as const;

export function useProfile() {
  return useQuery({ queryKey: profileKey, queryFn: meApi.get });
}

/** Keeps the cached session summary (name on home, guards) in step with the profile. */
async function syncSession(profile: Profile) {
  await updateUser((user) => ({ ...user, fullName: profile.fullName, profileCompleted: Boolean(profile.fullName) }));
}

export function useProfileMutation<TArgs>(run: (args: TArgs) => Promise<Profile>) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: async (profile) => {
      client.setQueryData(profileKey, profile);
      await syncSession(profile);
    },
  });
}

export const useUpdateProfile = () => useProfileMutation((request: UpdateProfileRequest) => meApi.updateProfile(request));
export const useUpdatePreferences = () => useProfileMutation((alertsEnabled: boolean) => meApi.updatePreferences(alertsEnabled));

export function useUpdateConsent() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ type, granted }: { type: 'LocationSharing' | 'HealthData'; granted: boolean }) => meApi.updateConsent(type, granted),
    onSuccess: (consents: ConsentState) => {
      client.setQueryData<Profile>(profileKey, (profile) => (profile ? { ...profile, consents } : profile));
      // Withdrawing health consent erases blood group and emergency contact on the server: reload.
      void client.invalidateQueries({ queryKey: profileKey });
    },
  });
}

/** Downloads GET /me/data-export and opens the share sheet (save to Files, email, …). */
export async function exportMyData(): Promise<void> {
  const data = await meApi.exportData();
  const stamp = new Date().toISOString().slice(0, 10).replace(/-/g, '');
  const file = new File(Paths.cache, `community-sos-data-${stamp}.json`);
  if (file.exists) file.delete();
  file.create();
  file.write(JSON.stringify(data, null, 2));
  try {
    await Sharing.shareAsync(file.uri, { mimeType: 'application/json', UTI: 'public.json', dialogTitle: file.name });
  } finally {
    // The share sheet has copied it by now; do not leave personal data in the cache.
    setTimeout(() => {
      try {
        if (file.exists) file.delete();
      } catch {
        // ignore
      }
    }, 60_000);
  }
}
