import { useRouter } from 'expo-router';
import { useTranslation } from 'react-i18next';
import { ActivityIndicator, Pressable, StyleSheet, Text, View } from 'react-native';

import type { ReferralEntry, ReferralTree } from '@/api/types';
import { useSessionStore } from '@/auth/sessionStore';
import { useReferralTree, useReferrals } from '@/features/admin/queries';
import { UserStatusPill } from '@/features/admin/UserStatusPill';
import { formatDate } from '@/lib/format';
import { Banner } from '@/ui/Banner';
import { describeError } from '@/ui/describeError';
import { PagedList } from '@/ui/PagedList';
import { colors, radius, spacing, typography } from '@/ui/theme';

/**
 * The permanent referral record. The Master Admin also gets the tree summary (one row per
 * Community Admin, tap for their people); a Sub-Admin sees only who joined through them.
 */
export default function ReferralsScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const isMaster = useSessionStore((s) => s.user?.role === 'MasterAdmin');
  const tree = useReferralTree(isMaster);
  const history = useReferrals();

  const header = (
    <View style={styles.header}>
      {isMaster ? (
        tree.data ? (
          <Tree tree={tree.data} onOpen={(id, name) => router.push({ pathname: '/admin/users/[id]', params: { id, name } })} />
        ) : tree.isError ? (
          <Banner tone="error" message={describeError(tree.error, t).message} />
        ) : (
          <ActivityIndicator color={colors.primary} />
        )
      ) : null}
      <Text style={styles.sectionTitle} accessibilityRole="header">
        {t('admin.referrals.historyTitle')}
      </Text>
    </View>
  );

  return (
    <PagedList
      query={history}
      keyExtractor={(item) => String(item.id)}
      emptyText={t('admin.referrals.empty')}
      header={header}
      renderItem={(item) => <ReferralRow entry={item} onPress={() => router.push({ pathname: '/admin/users/[id]', params: { id: item.referredUser.id } })} />}
    />
  );
}

function Tree({ tree, onOpen }: { tree: ReferralTree; onOpen: (id: string, name: string) => void }) {
  const { t } = useTranslation();
  return (
    <View style={styles.group}>
      <Text style={styles.sectionTitle} accessibilityRole="header">
        {t('admin.referrals.treeTitle')}
      </Text>
      <Text style={typography.body}>{t('admin.referrals.treeSummary', { subAdmins: tree.totalSubAdmins, users: tree.totalUsers })}</Text>
      {tree.subAdmins.map((node) => {
        const name = node.subAdmin.fullName ?? t('admin.users.noName');
        return (
          <Pressable
            key={node.subAdmin.id}
            accessibilityRole="button"
            onPress={() => onOpen(node.subAdmin.id, name)}
            style={({ pressed }) => [styles.row, pressed && styles.pressed]}
          >
            <View style={styles.top}>
              <Text style={[typography.heading, styles.flex]} numberOfLines={1}>
                {name}
              </Text>
              <UserStatusPill status={node.subAdmin.status} />
            </View>
            <Text style={typography.small}>
              {t('admin.referrals.subAdminStats', { active: node.activeUsers, referred: node.referredUsers, invitations: node.activeInvitations })}
            </Text>
            <Text style={typography.small}>{t('admin.users.joined', { date: formatDate(node.joinedAt) })}</Text>
          </Pressable>
        );
      })}
    </View>
  );
}

function ReferralRow({ entry, onPress }: { entry: ReferralEntry; onPress: () => void }) {
  const { t } = useTranslation();
  const name = entry.referredUser.status === 'Deleted' ? t('admin.users.deleted') : (entry.referredUser.fullName ?? t('admin.users.noName'));
  return (
    <Pressable accessibilityRole="button" onPress={onPress} style={({ pressed }) => [styles.row, pressed && styles.pressed]}>
      <View style={styles.top}>
        <Text style={[typography.label, styles.flex]} numberOfLines={1}>
          {name}
        </Text>
        <Text style={typography.small}>{formatDate(entry.registeredAt)}</Text>
      </View>
      <Text style={typography.small}>
        {t(`common.roles.${entry.assignedRole}`)} ·{' '}
        {t('admin.referrals.historyEntry', { referrer: entry.referrer.fullName ?? t(`common.roles.${entry.referrer.role}`) })}
      </Text>
      {entry.invitationLabel ? <Text style={typography.small}>{t('admin.referrals.viaLink', { label: entry.invitationLabel })}</Text> : null}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  header: { gap: spacing.lg },
  group: { gap: spacing.sm },
  sectionTitle: { ...typography.label, color: colors.textMuted, textTransform: 'uppercase', fontSize: 13 },
  row: { backgroundColor: colors.surface, borderRadius: radius.lg, padding: spacing.md, gap: spacing.xs },
  pressed: { backgroundColor: colors.border },
  top: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  flex: { flex: 1 },
});
