import { useRouter } from 'expo-router';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { StyleSheet, Text, View } from 'react-native';

import { MAX_INVITATION_LABEL, type Invitation } from '@/api/types';
import { useSessionStore } from '@/auth/sessionStore';
import { EXPIRY_PRESETS, expiryFromPreset, parseMaxUses, type ExpiryPreset } from '@/features/admin/logic';
import { useCreateInvitation } from '@/features/admin/queries';
import { shareInvitation } from '@/features/admin/share';
import { Banner } from '@/ui/Banner';
import { Button } from '@/ui/Button';
import { Chips } from '@/ui/Chips';
import { describeError, type ErrorDescription } from '@/ui/describeError';
import { Screen } from '@/ui/Screen';
import { TextField } from '@/ui/TextField';
import { colors, radius, spacing, typography } from '@/ui/theme';

/**
 * Create an invite link. The Master Admin's links make Community Admins, a Sub-Admin's links make
 * Members (decided by the API from the caller's role). On success the link is shown with Share.
 */
export default function NewInvitationScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const isMaster = useSessionStore((s) => s.user?.role === 'MasterAdmin');
  const create = useCreateInvitation();

  const [label, setLabel] = useState('');
  const [maxUses, setMaxUses] = useState('');
  const [expiry, setExpiry] = useState<ExpiryPreset>('30d');
  const [maxUsesError, setMaxUsesError] = useState(false);
  const [error, setError] = useState<ErrorDescription | null>(null);
  const [created, setCreated] = useState<Invitation | null>(null);

  const submit = () => {
    setError(null);
    const uses = parseMaxUses(maxUses);
    setMaxUsesError(!uses.ok);
    if (!uses.ok) return;
    create.mutate(
      { label: label.trim() || undefined, maxUses: uses.value, expiresAt: expiryFromPreset(expiry) },
      { onSuccess: setCreated, onError: (e) => setError(describeError(e, t)) },
    );
  };

  const share = (invitation: Invitation) => {
    shareInvitation(invitation, t).catch((e: unknown) => setError(describeError(e, t)));
  };

  if (created) {
    return (
      <Screen footer={<Button variant="ghost" title={t('admin.newInvitation.done')} onPress={() => router.back()} />}>
        <Banner tone="success" message={t('admin.newInvitation.created')} />
        <View style={styles.linkCard}>
          <Text style={styles.code} selectable>
            {created.code}
          </Text>
          <Text style={typography.body} selectable>
            {created.link}
          </Text>
          <Text style={typography.small}>{t('admin.invitations.joinsAs', { role: t(`common.roles.${created.inviteeRole}`) })}</Text>
        </View>
        <Text style={typography.small}>{t('admin.newInvitation.privacyNote')}</Text>
        {error ? <Banner tone="error" message={error.message} detail={error.reference} /> : null}
        <Button title={t('admin.newInvitation.share')} onPress={() => share(created)} />
      </Screen>
    );
  }

  return (
    <Screen footer={<Button title={t('admin.newInvitation.create')} onPress={submit} loading={create.isPending} />}>
      <Text style={typography.body}>{isMaster ? t('admin.newInvitation.roleNoteMaster') : t('admin.newInvitation.roleNoteSub')}</Text>
      <TextField
        label={t('admin.newInvitation.label')}
        placeholder={t('admin.newInvitation.labelPlaceholder')}
        value={label}
        onChangeText={setLabel}
        maxLength={MAX_INVITATION_LABEL}
        returnKeyType="next"
      />
      <TextField
        label={t('admin.newInvitation.maxUses')}
        hint={t('admin.newInvitation.maxUsesHint')}
        error={maxUsesError ? t('admin.newInvitation.maxUsesInvalid') : undefined}
        value={maxUses}
        onChangeText={(text) => {
          setMaxUses(text.replace(/[^\d]/g, ''));
          setMaxUsesError(false);
        }}
        keyboardType="number-pad"
        maxLength={5}
      />
      <View style={styles.group}>
        <Text style={typography.label}>{t('admin.newInvitation.expiry')}</Text>
        <Chips
          options={EXPIRY_PRESETS.map((p) => ({ value: p, label: t(`admin.newInvitation.expiryOptions.${p}`) }))}
          value={expiry}
          onChange={setExpiry}
          accessibilityLabel={t('admin.newInvitation.expiry')}
        />
      </View>
      <Text style={typography.small}>{t('admin.newInvitation.privacyNote')}</Text>
      {error ? <Banner tone="error" message={error.message} detail={error.reference} /> : null}
    </Screen>
  );
}

const styles = StyleSheet.create({
  group: { gap: spacing.sm },
  linkCard: { backgroundColor: colors.surface, borderRadius: radius.lg, padding: spacing.md, gap: spacing.sm },
  code: { fontSize: 24, fontWeight: '700', letterSpacing: 3, color: colors.text },
});
