import { useRouter } from 'expo-router';
import { useTranslation } from 'react-i18next';
import { ActivityIndicator, FlatList, Pressable, RefreshControl, StyleSheet, Text, View } from 'react-native';

import type { SosSummary } from '@/api/types';
import { useMySosList } from '@/features/sos/queries';
import { StatusBadge } from '@/features/sos/StatusBadge';
import { formatDateTime } from '@/lib/format';
import { Banner } from '@/ui/Banner';
import { Button } from '@/ui/Button';
import { describeError } from '@/ui/describeError';
import { colors, radius, spacing, typography } from '@/ui/theme';

export default function SosHistoryScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const query = useMySosList();
  const items = query.data?.pages.flatMap((page) => page.items) ?? [];

  const renderItem = ({ item }: { item: SosSummary }) => (
    <Pressable
      accessibilityRole="button"
      onPress={() => router.push({ pathname: '/sos/[id]', params: { id: item.id } })}
      style={({ pressed }) => [styles.row, pressed && styles.pressed]}
    >
      <View style={styles.rowTop}>
        <Text style={typography.label}>{t(`sos.category.${item.category}`)}</Text>
        <Text style={typography.small}>{item.referenceNo}</Text>
      </View>
      <StatusBadge status={item.status} />
      <Text style={typography.small}>{formatDateTime(item.createdAt)}</Text>
      {item.address ? (
        <Text style={typography.body} numberOfLines={2}>
          {item.address}
        </Text>
      ) : null}
      {item.photoCount > 0 ? <Text style={typography.small}>{t('sos.history.photos', { count: item.photoCount })}</Text> : null}
    </Pressable>
  );

  return (
    <FlatList
      style={styles.list}
      contentContainerStyle={styles.content}
      contentInsetAdjustmentBehavior="automatic"
      data={items}
      keyExtractor={(item) => item.id}
      renderItem={renderItem}
      refreshControl={<RefreshControl refreshing={query.isRefetching && !query.isFetchingNextPage} onRefresh={() => void query.refetch()} />}
      onEndReachedThreshold={0.5}
      onEndReached={() => {
        if (query.hasNextPage && !query.isFetchingNextPage) void query.fetchNextPage();
      }}
      ListEmptyComponent={
        query.isPending ? (
          <ActivityIndicator size="large" color={colors.primary} />
        ) : query.isError ? (
          <View style={styles.gap}>
            <Banner tone="error" message={describeError(query.error, t).message} />
            <Button variant="secondary" title={t('common.retry')} onPress={() => void query.refetch()} />
          </View>
        ) : (
          <Text style={[typography.body, styles.empty]}>{t('sos.history.empty')}</Text>
        )
      }
      ListFooterComponent={query.isFetchingNextPage ? <ActivityIndicator color={colors.primary} /> : undefined}
    />
  );
}

const styles = StyleSheet.create({
  list: { flex: 1, backgroundColor: colors.background },
  content: { padding: spacing.lg, gap: spacing.md },
  row: { backgroundColor: colors.surface, borderRadius: radius.lg, padding: spacing.md, gap: spacing.xs },
  rowTop: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center' },
  pressed: { backgroundColor: colors.border },
  empty: { textAlign: 'center', color: colors.textMuted, marginTop: spacing.xl },
  gap: { gap: spacing.md },
});
