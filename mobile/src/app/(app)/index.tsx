import { useRouter } from 'expo-router';
import { useTranslation } from 'react-i18next';
import { Pressable, StyleSheet, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { isAdminRole, useSessionStore } from '@/auth/sessionStore';
import { useReviewQueueCount } from '@/features/admin/queries';
import { useOpenAlerts } from '@/features/alerts/queries';
import { EmergencyNumbersList } from '@/features/EmergencyNumbersList';
import { BackgroundPrompt } from '@/features/location/background/BackgroundPrompt';
import { PushPrompt } from '@/features/push/PushPrompt';
import { formatDistance } from '@/lib/format';
import { useActiveSos } from '@/features/sos/queries';
import { useSosOutbox } from '@/features/sos/sosOutbox';
import { StatusBadge } from '@/features/sos/StatusBadge';
import { Banner } from '@/ui/Banner';
import { EmergencyCallBar } from '@/ui/EmergencyCallBar';
import { LinkRow, Section } from '@/ui/Rows';
import { Screen } from '@/ui/Screen';
import { colors, radius, spacing, typography } from '@/ui/theme';

const SOS_BUTTON_SIZE = 200;

export default function HomeScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const user = useSessionStore((s) => s.user);
  const isAdmin = isAdminRole(user);
  const active = useActiveSos();
  const pending = useSosOutbox((s) => s.pending);
  const sending = useSosOutbox((s) => s.sending);

  const firstName = user?.fullName?.split(' ')[0];
  const activeSos = active.data ?? null;
  // One open SOS per person: while one is active or still being delivered, the button opens it instead.
  const blocked = activeSos !== null || pending !== null;

  const outboxMessage = !pending
    ? null
    : pending.sosId
      ? t('home.sendingPhotos')
      : sending
        ? t('home.sending')
        : t('home.waiting');

  return (
    <SafeAreaView style={styles.safe} edges={['top']}>
      <Screen footer={<EmergencyCallBar />}>
        <View style={styles.header}>
          <Text style={typography.title} accessibilityRole="header">
            {firstName ? t('home.greeting', { name: firstName }) : t('home.greetingNoName')}
          </Text>
          {user ? <Text style={typography.small}>{t('home.role', { role: t(`common.roles.${user.role}`) })}</Text> : null}
        </View>

        {outboxMessage ? <Banner tone={pending?.sosId || sending ? 'info' : 'error'} message={outboxMessage} /> : null}

        {activeSos ? (
          <Pressable
            accessibilityRole="button"
            onPress={() => router.push({ pathname: '/sos/[id]', params: { id: activeSos.id } })}
            style={({ pressed }) => [styles.activeCard, pressed && styles.pressed]}
          >
            <Text style={typography.heading}>{t('home.activeTitle')}</Text>
            <StatusBadge status={activeSos.status} />
            <Text style={typography.body}>{t(`sos.statusHelp.${activeSos.status}`)}</Text>
            <Text style={styles.link}>{t('home.activeOpen', { referenceNo: activeSos.referenceNo })}</Text>
          </Pressable>
        ) : (
          <View style={styles.sosArea}>
            <Pressable
              accessibilityRole="button"
              accessibilityLabel={t('home.sosButton')}
              accessibilityHint={t('home.sosButtonHint')}
              disabled={blocked}
              onPress={() => router.push('/sos/new')}
              style={({ pressed }) => [styles.sosButton, pressed && styles.sosPressed, blocked && styles.sosDisabled]}
            >
              <Text style={styles.sosText}>{t('home.sosButton')}</Text>
            </Pressable>
            <Text style={[typography.body, styles.center]}>{t('home.sosButtonCaption')}</Text>
          </View>
        )}

        <NearbyCard />

        {isAdmin ? <AdminCard /> : null}

        <PushPrompt admin={isAdmin} />
        <BackgroundPrompt />

        <EmergencyNumbersList />

        <Section>
          <LinkRow title={t('home.alerts')} onPress={() => router.push('/alerts')} />
          <LinkRow title={t('home.history')} onPress={() => router.push('/sos')} />
          <LinkRow title={t('home.profile')} onPress={() => router.push('/profile')} />
        </Section>
      </Screen>
    </SafeAreaView>
  );
}

/** Someone within 5 km needs help (approved SOS this person was alerted about). */
function NearbyCard() {
  const { t } = useTranslation();
  const router = useRouter();
  const open = useOpenAlerts().data ?? [];
  if (open.length === 0) return null;
  const first = open[0]!;

  return (
    <Pressable
      accessibilityRole="button"
      onPress={() => (open.length === 1 ? router.push({ pathname: '/sos/[id]', params: { id: first.sosId } }) : router.push('/alerts'))}
      style={({ pressed }) => [styles.queueCard, pressed && styles.pressed]}
    >
      <Text style={typography.heading}>{t('home.nearbyTitle', { count: open.length })}</Text>
      <Text style={typography.body}>
        {t(`sos.category.${first.category}`)} · {t('alerts.away', { distance: formatDistance(first.distanceMeters) })}
      </Text>
      <Text style={styles.link}>{t('home.nearbyOpen')}</Text>
    </Pressable>
  );
}

/** Admins: SOS waiting for their review (refreshed by push, with polling as a safety net) and the way into the admin area. */
function AdminCard() {
  const { t } = useTranslation();
  const router = useRouter();
  const queue = useReviewQueueCount(true);
  const waiting = queue.data?.total ?? 0;
  const escalated = queue.data?.escalated ?? 0;

  return (
    <View style={styles.adminArea}>
      {waiting > 0 ? (
        <Pressable
          accessibilityRole="button"
          accessibilityHint={t('home.queueOpen')}
          onPress={() => router.push('/admin/queue')}
          style={({ pressed }) => [styles.queueCard, pressed && styles.pressed]}
        >
          <Text style={typography.heading}>{t('home.queueWaiting', { count: waiting })}</Text>
          {escalated > 0 ? <Text style={styles.escalated}>{t('home.queueEscalated', { count: escalated })}</Text> : null}
          <Text style={styles.link}>{t('home.queueOpen')}</Text>
        </Pressable>
      ) : null}
      <Section title={t('home.adminSection')}>
        <LinkRow title={t('home.adminTools')} onPress={() => router.push('/admin')} />
      </Section>
    </View>
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.background },
  adminArea: { gap: spacing.md },
  queueCard: {
    borderWidth: 2,
    borderColor: colors.primary,
    backgroundColor: colors.dangerSurface,
    borderRadius: radius.lg,
    padding: spacing.md,
    gap: spacing.xs,
  },
  escalated: { ...typography.label, color: colors.primary },
  header: { gap: spacing.xs },
  sosArea: { alignItems: 'center', gap: spacing.md, paddingVertical: spacing.md },
  sosButton: {
    width: SOS_BUTTON_SIZE,
    height: SOS_BUTTON_SIZE,
    borderRadius: SOS_BUTTON_SIZE / 2,
    backgroundColor: colors.primary,
    alignItems: 'center',
    justifyContent: 'center',
    borderWidth: 10,
    borderColor: colors.dangerSurface,
    elevation: 6,
    shadowColor: '#000',
    shadowOpacity: 0.25,
    shadowRadius: 8,
    shadowOffset: { width: 0, height: 4 },
  },
  sosPressed: { backgroundColor: colors.primaryPressed, transform: [{ scale: 0.97 }] },
  sosDisabled: { backgroundColor: colors.disabled },
  sosText: { color: colors.onPrimary, fontSize: 56, fontWeight: '800', letterSpacing: 4 },
  center: { textAlign: 'center' },
  activeCard: {
    borderWidth: 2,
    borderColor: colors.primary,
    borderRadius: radius.lg,
    padding: spacing.md,
    gap: spacing.sm,
  },
  pressed: { backgroundColor: colors.dangerSurface },
  link: { color: colors.primary, fontWeight: '700', fontSize: 16 },
});
