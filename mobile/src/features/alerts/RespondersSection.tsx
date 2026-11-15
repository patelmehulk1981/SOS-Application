import { useTranslation } from 'react-i18next';
import { StyleSheet, Text, View } from 'react-native';

import type { SosResponders } from '@/api/types';
import { formatDistance } from '@/lib/format';
import { Section } from '@/ui/Rows';
import { spacing, typography } from '@/ui/theme';

/** Requester and admin views: how many neighbours were alerted, saw it, and who is coming (first names only). */
export function RespondersSection({ responders }: { responders: SosResponders }) {
  const { t } = useTranslation();
  const coming = responders.canHelp + responders.onTheWay + responders.arrived;

  return (
    <Section title={t('sos.detail.respondersTitle')}>
      <View style={styles.row}>
        <Text style={typography.body}>
          {t('sos.detail.respondersSummary', { alerted: responders.alerted, acknowledged: responders.acknowledged, coming })}
        </Text>
      </View>
      {responders.helping.length === 0 ? (
        <View style={styles.row}>
          <Text style={typography.small}>{t('sos.detail.noResponders')}</Text>
        </View>
      ) : (
        responders.helping.map((person, index) => (
          <View key={`${person.respondedAt}-${index}`} style={styles.row}>
            <Text style={typography.label}>
              {t('sos.detail.responderLine', {
                name: person.firstName ?? t('sos.detail.responderSomeone'),
                response: t(`response.short.${person.response}`),
                distance: formatDistance(person.distanceMeters),
              })}
            </Text>
            {person.note ? <Text style={typography.body}>“{person.note}”</Text> : null}
          </View>
        ))
      )}
    </Section>
  );
}

const styles = StyleSheet.create({
  row: { paddingHorizontal: spacing.md, paddingVertical: spacing.sm, gap: 2 },
});
