import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Linking, Pressable, StyleSheet, Text, View } from 'react-native';

import { MAX_RESPONSE_NOTE, RESPONSE_CHOICES, type ResponseType, type SosDetail } from '@/api/types';
import { formatDistance } from '@/lib/format';
import { Banner } from '@/ui/Banner';
import { Button } from '@/ui/Button';
import { describeError } from '@/ui/describeError';
import { TextDialog } from '@/ui/TextDialog';
import { colors, radius, spacing, touchTarget, typography } from '@/ui/theme';

import { useAcknowledgeAlert, useRespondToAlert } from './queries';

type Choice = Exclude<ResponseType, 'None'>;

/** Google Maps directions to the SOS (app if installed, otherwise the browser). */
export function directionsUrl(latitude: number, longitude: number): string {
  return `https://www.google.com/maps/dir/?api=1&destination=${latitude},${longitude}`;
}

/**
 * Recipient view: what an alerted neighbour can do. Opening the SOS acknowledges the alert once; responses
 * can be changed while the SOS is open, and an optional note is shared with the requester and their admin.
 */
export function HelpPanel({ sos }: { sos: SosDetail }) {
  const { t } = useTranslation();
  const acknowledge = useAcknowledgeAlert();
  const respond = useRespondToAlert();
  const acknowledged = useRef(false);
  const [noteOpen, setNoteOpen] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const myAlert = sos.myAlert;
  const name = sos.requester.firstName;
  const current = myAlert?.response ?? 'None';
  const distance = myAlert ? formatDistance(myAlert.distanceMeters) : '';

  useEffect(() => {
    if (!myAlert || myAlert.acknowledgedAt || acknowledged.current) return;
    acknowledged.current = true;
    acknowledge.mutate(sos.id);
  }, [acknowledge, myAlert, sos.id]);

  const send = (response: Choice, note: string | undefined, onDone?: () => void) => {
    setError(null);
    respond.mutate(
      { sosId: sos.id, response, note },
      {
        onSuccess: () => {
          setSaved(true);
          onDone?.();
        },
        onError: (e) => setError(describeError(e, t).message),
      },
    );
  };

  return (
    <View style={styles.panel}>
      <Text style={typography.heading}>
        {name ? t('sos.detail.helpTitle', { name, distance }) : t('sos.detail.helpTitleNoName', { distance })}
      </Text>
      <Text style={typography.body}>{t('sos.detail.helpIntro')}</Text>
      <Button title={t('sos.detail.directions')} onPress={() => void Linking.openURL(directionsUrl(sos.location.latitude, sos.location.longitude))} />

      <Text style={styles.sectionTitle} accessibilityRole="header">
        {t('sos.detail.yourResponse')}
      </Text>
      <View style={styles.choices} accessibilityRole="radiogroup">
        {RESPONSE_CHOICES.map((choice) => {
          const selected = current === choice;
          return (
            <Pressable
              key={choice}
              accessibilityRole="radio"
              accessibilityState={{ selected, checked: selected, disabled: respond.isPending }}
              disabled={respond.isPending}
              onPress={() => send(choice, myAlert?.note)}
              style={({ pressed }) => [styles.choice, selected && styles.selected, pressed && !selected && styles.pressed]}
            >
              <Text style={[styles.choiceText, selected && styles.selectedText]}>{t(`response.choices.${choice}`)}</Text>
            </Pressable>
          );
        })}
      </View>

      {myAlert?.note ? <Text style={typography.small}>“{myAlert.note}”</Text> : null}
      <Button
        variant="ghost"
        title={myAlert?.note ? t('sos.detail.editNote') : t('sos.detail.addNote')}
        onPress={() => (current === 'None' ? setError(t('sos.detail.chooseFirst')) : setNoteOpen(true))}
      />

      {saved && !error ? (
        <Banner tone="success" message={name ? t('sos.detail.responseSaved', { name }) : t('sos.detail.responseSavedNoName')} />
      ) : null}
      {error ? <Banner tone="error" message={error} /> : null}

      <TextDialog
        visible={noteOpen}
        title={t('sos.detail.noteTitle')}
        label={t('sos.detail.noteLabel')}
        placeholder={t('sos.detail.notePlaceholder')}
        confirmTitle={t('sos.detail.noteSave')}
        cancelTitle={t('common.cancel')}
        maxLength={MAX_RESPONSE_NOTE}
        busy={respond.isPending}
        onConfirm={(note) => {
          if (current !== 'None') send(current, note || undefined, () => setNoteOpen(false));
        }}
        onCancel={() => setNoteOpen(false)}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  panel: { gap: spacing.sm, padding: spacing.md, borderRadius: radius.lg, borderWidth: 2, borderColor: colors.primary },
  sectionTitle: { ...typography.label, color: colors.textMuted, textTransform: 'uppercase', fontSize: 13, marginTop: spacing.sm },
  choices: { gap: spacing.sm },
  choice: {
    minHeight: touchTarget + 4,
    justifyContent: 'center',
    paddingHorizontal: spacing.md,
    borderRadius: radius.md,
    borderWidth: 1.5,
    borderColor: colors.border,
    backgroundColor: colors.background,
  },
  selected: { backgroundColor: colors.text, borderColor: colors.text },
  pressed: { backgroundColor: colors.surface },
  choiceText: { fontSize: 17, fontWeight: '600', color: colors.text },
  selectedText: { color: colors.background },
});
