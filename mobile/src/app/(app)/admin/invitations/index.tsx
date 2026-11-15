import { useRouter } from 'expo-router';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Alert, StyleSheet, Text, View } from 'react-native';

import type { Invitation, InvitationStatus } from '@/api/types';
import { useInvitations, useRevokeInvitation } from '@/features/admin/queries';
import { shareInvitation } from '@/features/admin/share';
import { formatDate } from '@/lib/format';
import { Banner } from '@/ui/Banner';
import { Button } from '@/ui/Button';
import { Chips } from '@/ui/Chips';
import { describeError } from '@/ui/describeError';
import { PagedList } from '@/ui/PagedList';
import { Pill, type PillTone } from '@/ui/Pill';
import { colors, radius, spacing, typography } from '@/ui/theme';

type Filter = InvitationStatus | 'all';
const FILTERS: readonly Filter[] = ['Active', 'all', 'Exhausted', 'Expired', 'Revoked'];
const TONES: Record<InvitationStatus, PillTone> = { Active: 'success', Revoked: 'neutral', Expired: 'neutral', Exhausted: 'warning' };

export default function InvitationsScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const [filter, setFilter] = useState<Filter>('Active');
  const query = useInvitations(filter === 'all' ? undefined : filter);
  const revoke = useRevokeInvitation();
  const [error, setError] = useState<string | null>(null);

  const confirmRevoke = (invitation: Invitation) => {
    Alert.alert(t('admin.invitations.revokeTitle'), t('admin.invitations.revokeBody'), [
      { text: t('common.cancel'), style: 'cancel' },
      {
        text: t('admin.invitations.revokeConfirm'),
        style: 'destructive',
        onPress: () => {
          setError(null);
          revoke.mutate(invitation.id, { onError: (e) => setError(describeError(e, t).message) });
        },
      },
    ]);
  };

  const share = (invitation: Invitation) => {
    shareInvitation(invitation, t).catch((e: unknown) => setError(describeError(e, t).message));
  };

  return (
    <PagedList
      query={query}
      keyExtractor={(item) => item.id}
      emptyText={filter === 'Active' || filter === 'all' ? t('admin.invitations.empty') : t('admin.invitations.emptyFiltered')}
      header={
        <View style={styles.header}>
          <Button title={t('admin.invitations.create')} onPress={() => router.push('/admin/invitations/new')} />
          {error ? <Banner tone="error" message={error} /> : null}
          <Chips
            options={FILTERS.map((f) => ({ value: f, label: f === 'all' ? t('admin.filterAll') : t(`admin.invitations.status.${f}`) }))}
            value={filter}
            onChange={setFilter}
          />
        </View>
      }
      renderItem={(item) => (
        <InvitationRow
          invitation={item}
          revoking={revoke.isPending && revoke.variables === item.id}
          onShare={() => share(item)}
          onRevoke={() => confirmRevoke(item)}
        />
      )}
    />
  );
}

function InvitationRow({
  invitation,
  revoking,
  onShare,
  onRevoke,
}: {
  invitation: Invitation;
  revoking: boolean;
  onShare: () => void;
  onRevoke: () => void;
}) {
  const { t } = useTranslation();
  const active = invitation.status === 'Active';
  const usage =
    invitation.maxUses !== undefined
      ? t('admin.invitations.usedOf', { used: invitation.usedCount, max: invitation.maxUses })
      : t('admin.invitations.joined', { count: invitation.usedCount });
  const expiry = invitation.expiresAt
    ? invitation.status === 'Expired'
      ? t('admin.invitations.expiredOn', { date: formatDate(invitation.expiresAt) })
      : t('admin.invitations.expires', { date: formatDate(invitation.expiresAt) })
    : t('admin.invitations.noExpiry');

  return (
    <View style={styles.row}>
      <View style={styles.top}>
        <Text style={[typography.heading, styles.flex]} numberOfLines={2}>
          {invitation.label ?? t('admin.invitations.untitled')}
        </Text>
        <Pill label={t(`admin.invitations.status.${invitation.status}`)} tone={TONES[invitation.status]} />
      </View>
      <Text style={styles.code} selectable>
        {invitation.code}
      </Text>
      <Text style={typography.small}>{t('admin.invitations.joinsAs', { role: t(`common.roles.${invitation.inviteeRole}`) })}</Text>
      <Text style={typography.small}>
        {usage} · {expiry}
      </Text>
      {active ? (
        <View style={styles.actions}>
          <View style={styles.flex}>
            <Button title={t('admin.invitations.share')} onPress={onShare} />
          </View>
          <View style={styles.flex}>
            <Button variant="secondary" title={t('admin.invitations.revoke')} onPress={onRevoke} loading={revoking} />
          </View>
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  header: { gap: spacing.md },
  row: { backgroundColor: colors.surface, borderRadius: radius.lg, padding: spacing.md, gap: spacing.xs },
  top: { flexDirection: 'row', alignItems: 'flex-start', gap: spacing.sm },
  flex: { flex: 1 },
  code: { fontSize: 18, fontWeight: '700', letterSpacing: 2, color: colors.text, fontVariant: ['tabular-nums'] },
  actions: { flexDirection: 'row', gap: spacing.sm, marginTop: spacing.sm },
});
