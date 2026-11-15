import * as Location from 'expo-location';
import { useRouter } from 'expo-router';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ActivityIndicator, StyleSheet, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import type { MapBounds, SosMapPoint, SosStatus } from '@/api/types';
import { env } from '@/config/env';
import { INDIA_REGION, PERIODS, periodStart, regionAround, regionToBounds, type Period, type Region } from '@/features/admin/logic';
import { useSosMap } from '@/features/admin/queries';
import { PIN_COLORS, SosMap } from '@/features/admin/SosMap';
import { formatDateTime } from '@/lib/format';
import { Banner } from '@/ui/Banner';
import { Button } from '@/ui/Button';
import { Chips } from '@/ui/Chips';
import { describeError } from '@/ui/describeError';
import { colors, radius, spacing, typography } from '@/ui/theme';

type StatusFilter = SosStatus | 'all';
const STATUS_FILTERS: readonly StatusFilter[] = ['all', 'Escalated', 'Pending', 'Approved', 'Resolved', 'Rejected'];
const LEGEND: readonly SosStatus[] = ['Escalated', 'Pending', 'Approved', 'Resolved', 'Rejected'];
/** The admin's own last-known position is used for the first view only if it is this recent. */
const START_FIX_MAX_AGE_MS = 24 * 3_600_000;

export default function AdminMapScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const [startRegion, setStartRegion] = useState<Region | null>(null);
  const [bounds, setBounds] = useState<MapBounds | null>(null);
  const [status, setStatus] = useState<StatusFilter>('all');
  const [period, setPeriod] = useState<Period>('7d');
  const from = useMemo(() => periodStart(period), [period]);
  const query = useSosMap(bounds, status === 'all' ? undefined : status, from);

  useEffect(() => {
    // Start around the admin if location is already allowed (never prompts here), otherwise all of India.
    let cancelled = false;
    void (async () => {
      let region = INDIA_REGION;
      try {
        const permission = await Location.getForegroundPermissionsAsync();
        if (permission.granted) {
          const last = await Location.getLastKnownPositionAsync({ maxAge: START_FIX_MAX_AGE_MS });
          if (last) region = regionAround(last.coords.latitude, last.coords.longitude);
        }
      } catch {
        // Location unavailable: keep the India view.
      }
      if (!cancelled) setStartRegion(region);
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const onRegionChange = useCallback((region: Region) => {
    const next = regionToBounds(region);
    // Snapped bounds are often unchanged after a small pan: keep the same object so nothing refetches.
    setBounds((current) => (current && sameBounds(current, next) ? current : next));
  }, []);

  const onOpen = useCallback((id: string) => router.push({ pathname: '/sos/[id]', params: { id } }), [router]);

  const describe = useCallback(
    (point: SosMapPoint) => ({
      title: `${t(`sos.category.${point.category}`)} · ${point.referenceNo}`,
      description: `${t('admin.map.calloutHint', { status: t(`sos.status.${point.status}`) })}\n${formatDateTime(point.createdAt)}`,
    }),
    [t],
  );

  if (!env.mapsConfigured) {
    return (
      <View style={styles.fallback}>
        <Banner tone="info" message={t('admin.map.notConfigured')} />
        <Button variant="secondary" title={t('admin.map.openList')} onPress={() => router.replace('/admin/sos')} />
      </View>
    );
  }

  const points = query.data?.points ?? [];

  return (
    <SafeAreaView style={styles.safe} edges={['bottom', 'left', 'right']}>
      <View style={styles.filters}>
        <Chips
          options={STATUS_FILTERS.map((s) => ({ value: s, label: s === 'all' ? t('admin.filterAll') : t(`sos.status.${s}`) }))}
          value={status}
          onChange={setStatus}
        />
        <Chips options={PERIODS.map((p) => ({ value: p, label: t(`admin.periods.${p}`) }))} value={period} onChange={setPeriod} />
      </View>

      <View style={styles.mapArea}>
        {startRegion ? (
          <SosMap initialRegion={startRegion} points={points} describe={describe} onRegionChange={onRegionChange} onOpen={onOpen} />
        ) : (
          <ActivityIndicator size="large" color={colors.primary} style={styles.spinner} />
        )}

        <View style={styles.overlay} pointerEvents="box-none">
          {query.isError ? (
            <Banner tone="error" message={describeError(query.error, t).message} />
          ) : query.data?.truncated ? (
            <Banner tone="info" message={t('admin.map.truncated')} />
          ) : null}
          <View style={styles.status} accessibilityLiveRegion="polite">
            {query.isFetching ? <ActivityIndicator size="small" color={colors.primary} /> : null}
            <Text style={typography.label}>{t('admin.map.count', { count: points.length })}</Text>
          </View>
        </View>
      </View>

      <View style={styles.legend} accessible={false} importantForAccessibility="no-hide-descendants">
        {LEGEND.map((s) => (
          <View key={s} style={styles.legendItem}>
            <View style={[styles.dot, { backgroundColor: PIN_COLORS[s] }]} />
            <Text style={typography.small}>{t(`sos.status.${s}`)}</Text>
          </View>
        ))}
      </View>
    </SafeAreaView>
  );
}

function sameBounds(a: MapBounds, b: MapBounds): boolean {
  return (
    a.minLatitude === b.minLatitude &&
    a.maxLatitude === b.maxLatitude &&
    a.minLongitude === b.minLongitude &&
    a.maxLongitude === b.maxLongitude
  );
}

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.background },
  fallback: { flex: 1, padding: spacing.lg, gap: spacing.md, backgroundColor: colors.background },
  filters: { paddingHorizontal: spacing.md, paddingBottom: spacing.sm, gap: spacing.xs },
  mapArea: { flex: 1 },
  spinner: { marginTop: spacing.xl },
  overlay: { position: 'absolute', top: spacing.sm, left: spacing.sm, right: spacing.sm, gap: spacing.sm },
  status: {
    alignSelf: 'flex-start',
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    backgroundColor: colors.background,
    borderRadius: radius.pill,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.xs,
    elevation: 2,
    shadowColor: '#000',
    shadowOpacity: 0.15,
    shadowRadius: 4,
    shadowOffset: { width: 0, height: 1 },
  },
  legend: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.md, padding: spacing.md },
  legendItem: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs },
  dot: { width: 12, height: 12, borderRadius: 6 },
});
