import type { InfiniteData, UseInfiniteQueryResult } from '@tanstack/react-query';
import type { ReactElement } from 'react';
import { useTranslation } from 'react-i18next';
import { ActivityIndicator, FlatList, RefreshControl, StyleSheet, Text, View } from 'react-native';

import type { PagedResult } from '@/api/types';

import { Banner } from './Banner';
import { Button } from './Button';
import { describeError } from './describeError';
import { colors, spacing, typography } from './theme';

/**
 * A FlatList over a paged API query: pull to refresh, load the next page near the end, and
 * loading / error / empty states. Used by the admin lists.
 */
export function PagedList<T>({
  query,
  renderItem,
  keyExtractor,
  emptyText,
  header,
}: {
  query: UseInfiniteQueryResult<InfiniteData<PagedResult<T>>>;
  renderItem: (item: T) => ReactElement;
  keyExtractor: (item: T) => string;
  emptyText: string;
  header?: ReactElement;
}) {
  const { t } = useTranslation();
  const items = query.data?.pages.flatMap((page) => page.items) ?? [];
  // A filter change shows the previous results until the new ones arrive; a failed refresh keeps them too.
  const showError = query.isError && items.length === 0;

  return (
    <FlatList
      style={styles.list}
      contentContainerStyle={styles.content}
      contentInsetAdjustmentBehavior="automatic"
      keyboardShouldPersistTaps="handled"
      data={items}
      keyExtractor={keyExtractor}
      renderItem={({ item }) => renderItem(item)}
      ListHeaderComponent={header}
      refreshControl={
        <RefreshControl refreshing={query.isRefetching && !query.isFetchingNextPage && !query.isPlaceholderData} onRefresh={() => void query.refetch()} />
      }
      onEndReachedThreshold={0.5}
      onEndReached={() => {
        if (query.hasNextPage && !query.isFetchingNextPage) void query.fetchNextPage();
      }}
      ListEmptyComponent={
        query.isPending ? (
          <ActivityIndicator size="large" color={colors.primary} style={styles.spinner} />
        ) : showError ? (
          <View style={styles.gap}>
            <Banner tone="error" message={describeError(query.error, t).message} />
            <Button variant="secondary" title={t('common.retry')} onPress={() => void query.refetch()} />
          </View>
        ) : (
          <Text style={[typography.body, styles.empty]}>{emptyText}</Text>
        )
      }
      ListFooterComponent={query.isFetchingNextPage ? <ActivityIndicator color={colors.primary} /> : undefined}
    />
  );
}

const styles = StyleSheet.create({
  list: { flex: 1, backgroundColor: colors.background },
  content: { padding: spacing.lg, gap: spacing.md },
  spinner: { marginTop: spacing.xl },
  empty: { textAlign: 'center', color: colors.textMuted, marginTop: spacing.xl },
  gap: { gap: spacing.md },
});
