import { Stack, useLocalSearchParams, useRouter } from 'expo-router';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Pressable, StyleSheet, Text, View } from 'react-native';

import type { AdminUserListItem, UserRole, UserStatus } from '@/api/types';
import { useSessionStore } from '@/auth/sessionStore';
import { useAdminUsers } from '@/features/admin/queries';
import { UserStatusPill } from '@/features/admin/UserStatusPill';
import { formatDate } from '@/lib/format';
import { formatPhone } from '@/lib/phone';
import { useDebouncedValue } from '@/lib/useDebouncedValue';
import { Chips } from '@/ui/Chips';
import { PagedList } from '@/ui/PagedList';
import { TextField } from '@/ui/TextField';
import { colors, radius, spacing, typography } from '@/ui/theme';

type RoleFilter = Exclude<UserRole, 'MasterAdmin'> | 'all';
type StatusFilter = Exclude<UserStatus, 'Deleted'> | 'all';

/**
 * People in the admin's scope: a Sub-Admin sees whom they invited; the Master Admin sees everyone,
 * or one Sub-Admin's people (`referredBy` + `name`, from the referral tree or a member screen).
 */
export default function AdminUsersScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const params = useLocalSearchParams<{ referredBy?: string; name?: string }>();
  const isMaster = useSessionStore((s) => s.user?.role === 'MasterAdmin');
  const [search, setSearch] = useState('');
  const [role, setRole] = useState<RoleFilter>('all');
  const [status, setStatus] = useState<StatusFilter>('all');
  const debouncedSearch = useDebouncedValue(search.trim(), 400);

  const query = useAdminUsers({
    search: debouncedSearch || undefined,
    role: role === 'all' ? undefined : role,
    status: status === 'all' ? undefined : status,
    referredBy: isMaster ? params.referredBy : undefined,
  });

  const header = (
    <View style={styles.header}>
      <TextField
        label={t('admin.users.search')}
        value={search}
        onChangeText={setSearch}
        autoCorrect={false}
        autoCapitalize="none"
        returnKeyType="search"
        maxLength={100}
        clearButtonMode="while-editing"
      />
      {isMaster && !params.referredBy ? (
        <Chips
          options={[
            { value: 'all', label: t('admin.filterAll') },
            { value: 'SubAdmin', label: t('admin.users.roleSubAdmins') },
            { value: 'User', label: t('admin.users.roleUsers') },
          ]}
          value={role}
          onChange={setRole}
        />
      ) : null}
      <Chips
        options={[
          { value: 'all', label: t('admin.filterAll') },
          { value: 'Active', label: t('admin.users.statusActive') },
          { value: 'Suspended', label: t('admin.users.statusSuspended') },
        ]}
        value={status}
        onChange={setStatus}
      />
    </View>
  );

  return (
    <>
      {params.name ? <Stack.Screen options={{ title: t('admin.users.titleFor', { name: params.name }) }} /> : null}
      <PagedList
        query={query}
        keyExtractor={(item) => item.id}
        emptyText={t('admin.users.empty')}
        header={header}
        renderItem={(item) => (
          <UserRow user={item} showReferrer={isMaster && !params.referredBy} onPress={() => router.push({ pathname: '/admin/users/[id]', params: { id: item.id } })} />
        )}
      />
    </>
  );
}

function UserRow({ user, showReferrer, onPress }: { user: AdminUserListItem; showReferrer: boolean; onPress: () => void }) {
  const { t } = useTranslation();
  const name = user.status === 'Deleted' ? t('admin.users.deleted') : (user.fullName ?? t('admin.users.noName'));
  return (
    <Pressable accessibilityRole="button" onPress={onPress} style={({ pressed }) => [styles.row, pressed && styles.pressed]}>
      <View style={styles.top}>
        <Text style={[typography.heading, styles.flex]} numberOfLines={1}>
          {name}
        </Text>
        <UserStatusPill status={user.status} />
      </View>
      <Text style={typography.small}>
        {t(`common.roles.${user.role}`)}
        {user.phoneNumber ? ` · ${formatPhone(user.phoneNumber)}` : ''}
      </Text>
      <Text style={typography.small}>
        {[
          t('admin.users.joined', { date: formatDate(user.createdAt) }),
          showReferrer && user.referredBy ? t('admin.users.invitedBy', { name: user.referredBy.fullName ?? t(`common.roles.${user.referredBy.role}`) }) : null,
          user.role === 'SubAdmin' ? t('admin.users.invitedCount', { count: user.referredUsersCount }) : null,
        ]
          .filter(Boolean)
          .join(' · ')}
      </Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  header: { gap: spacing.md },
  row: { backgroundColor: colors.surface, borderRadius: radius.lg, padding: spacing.md, gap: spacing.xs },
  pressed: { backgroundColor: colors.border },
  top: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  flex: { flex: 1 },
});
