import { useLocalSearchParams, useRouter } from 'expo-router';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ActivityIndicator, Alert, RefreshControl, ScrollView, StyleSheet, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { isApiError } from '@/api/errors';
import { MAX_SUSPEND_REASON, type AdminUserDetail } from '@/api/types';
import { useSessionStore } from '@/auth/sessionStore';
import { useAdminUser, useSetUserStatus } from '@/features/admin/queries';
import { UserStatusPill } from '@/features/admin/UserStatusPill';
import { formatDate, formatDateTime } from '@/lib/format';
import { formatPhone } from '@/lib/phone';
import { Banner } from '@/ui/Banner';
import { Button } from '@/ui/Button';
import { describeError, type ErrorDescription } from '@/ui/describeError';
import { callNumber } from '@/ui/EmergencyCallBar';
import { InfoRow, LinkRow, Section } from '@/ui/Rows';
import { TextDialog } from '@/ui/TextDialog';
import { colors, spacing, typography } from '@/ui/theme';

/**
 * One person in the admin's scope. Health data and location are deliberately not shown here
 * (docs/03c); the reviewing admin sees them on the SOS. Suspend / reactivate: Master Admin only.
 */
export default function AdminUserScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { t } = useTranslation();
  const query = useAdminUser(id);

  if (query.isPending) {
    return (
      <View style={styles.center}>
        <ActivityIndicator size="large" color={colors.primary} />
      </View>
    );
  }
  if (query.isError || !query.data) {
    // Outside the admin's scope is a 404 too (docs/03c), so it reads as "not found".
    const notFound = isApiError(query.error) && query.error.status === 404;
    return (
      <View style={[styles.center, styles.padded]}>
        <Banner tone="error" message={notFound ? t('admin.users.empty') : describeError(query.error, t).message} />
        {!notFound ? <Button variant="secondary" title={t('common.retry')} onPress={() => void query.refetch()} /> : null}
      </View>
    );
  }

  return <UserDetail detail={query.data} refreshing={query.isRefetching} onRefresh={() => void query.refetch()} />;
}

function UserDetail({ detail, refreshing, onRefresh }: { detail: AdminUserDetail; refreshing: boolean; onRefresh: () => void }) {
  const { t } = useTranslation();
  const router = useRouter();
  const me = useSessionStore((s) => s.user);
  const setStatus = useSetUserStatus();
  const [suspendOpen, setSuspendOpen] = useState(false);
  const [dialogError, setDialogError] = useState<string | null>(null);
  const [error, setError] = useState<ErrorDescription | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const user = detail.user;
  const isMaster = me?.role === 'MasterAdmin';
  const deleted = user.status === 'Deleted';
  const name = deleted ? t('admin.users.deleted') : (user.fullName ?? t('admin.users.noName'));
  // The API refuses these too (user.cannot_change_own_status / cannot_suspend_master / user.deleted).
  const canChangeStatus = isMaster && user.id !== me?.id && user.role !== 'MasterAdmin' && !deleted;
  const phone = user.phoneNumber;

  const suspend = (reason: string) => {
    setDialogError(null);
    setStatus.mutate(
      { id: user.id, status: 'Suspended', reason },
      {
        onSuccess: () => {
          setSuspendOpen(false);
          setNotice(t('admin.user.suspended'));
        },
        onError: (e) => setDialogError(describeError(e, t).message),
      },
    );
  };

  const reactivate = () => {
    Alert.alert(t('admin.user.reactivateTitle', { name }), t('admin.user.reactivateBody'), [
      { text: t('common.cancel'), style: 'cancel' },
      {
        text: t('admin.user.reactivateConfirm'),
        onPress: () => {
          setError(null);
          setStatus.mutate(
            { id: user.id, status: 'Active' },
            { onSuccess: () => setNotice(t('admin.user.reactivated')), onError: (e) => setError(describeError(e, t)) },
          );
        },
      },
    ]);
  };

  return (
    <SafeAreaView style={styles.safe} edges={['bottom', 'left', 'right']}>
      <ScrollView
        contentContainerStyle={styles.content}
        contentInsetAdjustmentBehavior="automatic"
        refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} />}
      >
        <View style={styles.header}>
          <Text style={typography.title}>{name}</Text>
          <Text style={typography.body}>{t(`common.roles.${user.role}`)}</Text>
          <UserStatusPill status={user.status} />
        </View>

        {notice ? <Banner tone="success" message={notice} /> : null}

        {phone ? (
          <Button
            variant="secondary"
            title={formatPhone(phone)}
            accessibilityHint={t('emergency.callLabel', { label: name, number: phone })}
            onPress={() => void callNumber(phone, t('emergency.callFailed', { number: phone }))}
          />
        ) : null}

        <Section>
          {detail.email ? <InfoRow label={t('admin.user.email')} value={detail.email} /> : null}
          <InfoRow label={t('admin.user.joined')} value={formatDate(user.createdAt)} />
          <InfoRow label={t('admin.user.lastLogin')} value={user.lastLoginAt ? formatDateTime(user.lastLoginAt) : t('admin.user.never')} />
          {user.referredBy ? (
            <InfoRow label={t('admin.user.invitedBy')} value={user.referredBy.fullName ?? t(`common.roles.${user.referredBy.role}`)} />
          ) : null}
          {detail.referral?.invitationLabel ? <InfoRow label={t('admin.user.inviteLabel')} value={detail.referral.invitationLabel} /> : null}
          <InfoRow label={t('admin.user.sosRaised')} value={String(detail.sosRaised)} />
          <InfoRow label={t('admin.user.alerts')} value={user.alertsEnabled ? t('admin.user.alertsOn') : t('admin.user.alertsOff')} />
          {detail.acceptedPolicyVersion ? <InfoRow label={t('admin.user.policy')} value={detail.acceptedPolicyVersion} /> : null}
        </Section>

        {user.role === 'SubAdmin' ? (
          <Section>
            <InfoRow label={t('admin.user.invitedPeople')} value={String(user.referredUsersCount)} />
            <InfoRow label={t('admin.user.activeInvitations')} value={String(detail.activeInvitations)} />
            {isMaster ? (
              <>
                <LinkRow
                  title={t('admin.user.viewInvited')}
                  onPress={() => router.push({ pathname: '/admin/users', params: { referredBy: user.id, name } })}
                />
                <LinkRow
                  title={t('admin.user.viewSos')}
                  onPress={() => router.push({ pathname: '/admin/sos', params: { assignedAdminId: user.id, name } })}
                />
              </>
            ) : null}
          </Section>
        ) : null}

        <Text style={typography.small}>{t('admin.user.healthNote')}</Text>

        {error ? <Banner tone="error" message={error.message} detail={error.reference} /> : null}

        {canChangeStatus && user.status === 'Active' ? (
          <View style={styles.group}>
            <Text style={typography.small}>{user.role === 'SubAdmin' ? t('admin.user.suspendHelpSubAdmin') : t('admin.user.suspendHelp')}</Text>
            <Button
              variant="secondary"
              title={t('admin.user.suspend')}
              onPress={() => {
                setNotice(null);
                setSuspendOpen(true);
              }}
            />
          </View>
        ) : null}
        {canChangeStatus && user.status === 'Suspended' ? (
          <Button title={t('admin.user.reactivate')} onPress={reactivate} loading={setStatus.isPending} />
        ) : null}
      </ScrollView>

      <TextDialog
        visible={suspendOpen}
        title={t('admin.user.suspendTitle', { name })}
        label={t('admin.user.suspendLabel')}
        placeholder={t('admin.user.suspendPlaceholder')}
        confirmTitle={t('admin.user.suspendConfirm')}
        cancelTitle={t('common.cancel')}
        maxLength={MAX_SUSPEND_REASON}
        required
        busy={setStatus.isPending}
        error={dialogError}
        onConfirm={suspend}
        onCancel={() => {
          setSuspendOpen(false);
          setDialogError(null);
        }}
      />
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.background },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: spacing.md, backgroundColor: colors.background },
  padded: { padding: spacing.lg },
  content: { padding: spacing.lg, gap: spacing.lg },
  header: { gap: spacing.sm },
  group: { gap: spacing.sm },
});
