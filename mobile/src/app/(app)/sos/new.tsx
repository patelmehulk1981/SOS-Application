import * as Crypto from 'expo-crypto';
import { Image } from 'expo-image';
import { useRouter } from 'expo-router';
import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Pressable, StyleSheet, Text, View } from 'react-native';

import { isApiError } from '@/api/errors';
import { MAX_SOS_DESCRIPTION, MAX_SOS_PHOTOS, SOS_CATEGORIES, type SosCategory } from '@/api/types';
import { useCurrentLocation, type CurrentLocation } from '@/features/location/useCurrentLocation';
import type { OutboxOutcome } from '@/features/sos/outbox';
import { deletePhotos, pickPhotos, type LocalPhoto, type PhotoSource } from '@/features/sos/photos';
import { submitSos, useSosOutbox } from '@/features/sos/sosOutbox';
import { roundAccuracy } from '@/lib/format';
import { Banner } from '@/ui/Banner';
import { Button } from '@/ui/Button';
import { describeError } from '@/ui/describeError';
import { EmergencyCallBar } from '@/ui/EmergencyCallBar';
import { Screen } from '@/ui/Screen';
import { TextField } from '@/ui/TextField';
import { colors, radius, spacing, touchTarget, typography } from '@/ui/theme';

type Notice = { tone: 'error' | 'info'; message: string; detail?: string };

export default function NewSosScreen() {
  const { t } = useTranslation();
  const router = useRouter();
  const location = useCurrentLocation();

  // One id per SOS press, reused on every retry: the API turns duplicates into replays (docs/03d).
  const [clientRequestId] = useState(() => Crypto.randomUUID());
  const [category, setCategory] = useState<SosCategory | null>(null);
  const [description, setDescription] = useState('');
  const [photos, setPhotos] = useState<LocalPhoto[]>([]);
  const [sending, setSending] = useState(false);
  const [queued, setQueued] = useState(false);
  const [notice, setNotice] = useState<Notice | null>(null);
  const handedOff = useRef(false);
  const photosRef = useRef<LocalPhoto[]>([]);
  photosRef.current = photos;

  // Photos picked but never sent are deleted when the screen closes; once queued, the outbox owns them.
  useEffect(
    () => () => {
      if (!handedOff.current) deletePhotos(photosRef.current.map((p) => p.uri));
    },
    [],
  );

  // A queued SOS that gets delivered in the background (connection back) opens its detail page.
  const outboxPending = useSosOutbox((s) => s.pending);
  const lastOutcome = useSosOutbox((s) => s.lastOutcome);
  useEffect(() => {
    if (!queued) return;
    if (outboxPending?.clientRequestId === clientRequestId && outboxPending.sosId) {
      router.replace({ pathname: '/sos/[id]', params: { id: outboxPending.sosId } });
    } else if (!outboxPending && lastOutcome) {
      handleOutcome(lastOutcome);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [queued, outboxPending, lastOutcome]);

  const addPhotos = async (source: PhotoSource) => {
    setNotice(null);
    try {
      const result = await pickPhotos(source, MAX_SOS_PHOTOS - photos.length);
      if (result.kind === 'picked') setPhotos((current) => [...current, ...result.photos].slice(0, MAX_SOS_PHOTOS));
      if (result.kind === 'permissionDenied') setNotice({ tone: 'error', message: t('sos.new.cameraDenied') });
    } catch (e) {
      setNotice({ tone: 'error', ...describeError(e, t) });
    }
  };

  const removePhoto = (uri: string) => {
    deletePhotos([uri]);
    setPhotos((current) => current.filter((p) => p.uri !== uri));
  };

  function handleOutcome(outcome: OutboxOutcome) {
    switch (outcome.kind) {
      case 'sent':
      case 'photosPending':
        router.replace({ pathname: '/sos/[id]', params: { id: outcome.sosId } });
        return;
      case 'retryLater':
        setQueued(true);
        setNotice({ tone: 'error', message: t('sos.new.queued') });
        return;
      case 'alreadyOpen':
        if (outcome.sosId) router.replace({ pathname: '/sos/[id]', params: { id: outcome.sosId } });
        else router.back();
        return;
      case 'expired':
        setQueued(false);
        handedOff.current = false;
        setNotice({ tone: 'error', message: t('sos.new.expired') });
        return;
      case 'rejected':
        setQueued(false);
        setNotice(
          isApiError(outcome.error, 'sos.no_admin')
            ? { tone: 'error', message: t('sos.new.noAdmin') }
            : { tone: 'error', ...describeError(outcome.error, t) },
        );
        return;
    }
  }

  const send = async () => {
    if (!category) {
      setNotice({ tone: 'error', message: t('sos.new.chooseCategory') });
      return;
    }
    const fix = location.fix;
    if (!fix) return;

    setSending(true);
    setNotice(null);
    handedOff.current = true;
    const trimmed = description.trim();
    try {
      const outcome = await submitSos({
        clientRequestId,
        queuedAt: new Date().toISOString(),
        photoUris: photos.map((p) => p.uri),
        request: {
          clientRequestId,
          category,
          description: trimmed === '' ? undefined : trimmed,
          latitude: fix.latitude,
          longitude: fix.longitude,
          accuracyMeters: fix.accuracyMeters,
          capturedAt: fix.capturedAt,
        },
      });
      if (outcome.kind === 'rejected' || outcome.kind === 'expired') {
        // The outbox deleted the photo files; clear them here too.
        setPhotos([]);
      }
      handleOutcome(outcome);
    } catch (e) {
      // Local storage failure: nothing was queued, so the photos are still ours.
      handedOff.current = false;
      setNotice({ tone: 'error', ...describeError(e, t) });
    } finally {
      setSending(false);
    }
  };

  const canSend = location.fix !== null && !sending && !queued;

  return (
    <Screen
      footer={
        <>
          <Button title={sending ? t('sos.new.sending') : t('sos.new.send')} onPress={send} loading={sending} disabled={!canSend} />
          <EmergencyCallBar />
          {/* Once queued, leaving is safe: delivery continues in the background. */}
          <Button
            variant="ghost"
            title={queued ? t('sos.new.backHome') : t('common.cancel')}
            onPress={() => router.back()}
            disabled={sending}
          />
        </>
      }
    >
      <Banner tone="info" message={t('sos.new.call112First')} />
      <LocationStatus location={location} />

      <View style={styles.group}>
        <Text style={typography.label}>{t('sos.new.categoryLabel')}</Text>
        <View style={styles.categories} accessibilityRole="radiogroup">
          {SOS_CATEGORIES.map((item) => {
            const selected = category === item;
            return (
              <Pressable
                key={item}
                accessibilityRole="radio"
                accessibilityState={{ selected, disabled: queued }}
                disabled={queued}
                onPress={() => setCategory(item)}
                style={[styles.category, selected && styles.categorySelected]}
              >
                <Text style={[styles.categoryText, selected && styles.categoryTextSelected]}>{t(`sos.category.${item}`)}</Text>
              </Pressable>
            );
          })}
        </View>
      </View>

      <TextField
        label={t('sos.new.descriptionLabel')}
        placeholder={t('sos.new.descriptionPlaceholder')}
        value={description}
        onChangeText={setDescription}
        multiline
        maxLength={MAX_SOS_DESCRIPTION}
        editable={!queued}
        style={styles.description}
      />

      <View style={styles.group}>
        <Text style={typography.label}>{t('sos.new.photosLabel', { max: MAX_SOS_PHOTOS })}</Text>
        {photos.length > 0 ? (
          <View style={styles.photos}>
            {photos.map((photo, index) => (
              <Pressable
                key={photo.uri}
                accessibilityRole="button"
                accessibilityLabel={t('sos.new.removePhoto', { index: index + 1 })}
                disabled={queued}
                onPress={() => removePhoto(photo.uri)}
              >
                <Image source={{ uri: photo.uri }} style={styles.photo} contentFit="cover" />
                {!queued ? <Text style={styles.removeBadge}>✕</Text> : null}
              </Pressable>
            ))}
          </View>
        ) : null}
        {photos.length < MAX_SOS_PHOTOS && !queued ? (
          <View style={styles.photoButtons}>
            <View style={styles.flex}>
              <Button variant="secondary" title={t('sos.new.addCamera')} onPress={() => void addPhotos('camera')} />
            </View>
            <View style={styles.flex}>
              <Button variant="secondary" title={t('sos.new.addLibrary')} onPress={() => void addPhotos('library')} />
            </View>
          </View>
        ) : null}
      </View>

      {notice ? <Banner tone={notice.tone} message={notice.message} detail={notice.detail} /> : null}
    </Screen>
  );
}

function LocationStatus({ location }: { location: CurrentLocation }) {
  const { t } = useTranslation();
  const accuracy = roundAccuracy(location.fix?.accuracyMeters);

  switch (location.status) {
    case 'denied':
      return (
        <View style={styles.group}>
          <Banner tone="error" message={t('sos.location.denied')} />
          {location.canAskAgain ? (
            <Button variant="secondary" title={t('sos.location.allow')} onPress={location.retry} />
          ) : (
            <Button variant="secondary" title={t('sos.location.openSettings')} onPress={location.openSettings} />
          )}
        </View>
      );
    case 'servicesOff':
      return (
        <View style={styles.group}>
          <Banner tone="error" message={t('sos.location.servicesOff')} />
          <Button variant="secondary" title={t('sos.location.turnOn')} onPress={location.enableServices} />
        </View>
      );
    case 'unavailable':
      return (
        <View style={styles.group}>
          <Banner tone="error" message={t('sos.location.unavailable')} />
          <Button variant="secondary" title={t('sos.location.retry')} onPress={location.retry} />
        </View>
      );
    case 'ready':
      return <Banner tone="success" message={t('sos.location.ready', { accuracy: accuracy ?? '?' })} />;
    case 'locating':
      if (!location.fix) return <Banner tone="info" message={t('sos.location.waiting')} />;
      return (
        <Banner
          tone="info"
          message={accuracy !== undefined ? t('sos.location.locating', { accuracy }) : t('sos.location.locatingNoAccuracy')}
        />
      );
    default:
      return <Banner tone="info" message={t('sos.location.checking')} />;
  }
}

const PHOTO_SIZE = 88;

const styles = StyleSheet.create({
  group: { gap: spacing.sm },
  flex: { flex: 1 },
  categories: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  category: {
    minHeight: touchTarget + 8,
    minWidth: '47%',
    flexGrow: 1,
    borderWidth: 2,
    borderColor: colors.border,
    borderRadius: radius.md,
    alignItems: 'center',
    justifyContent: 'center',
    paddingHorizontal: spacing.md,
  },
  categorySelected: { borderColor: colors.primary, backgroundColor: colors.dangerSurface },
  categoryText: { fontSize: 17, fontWeight: '600', color: colors.text },
  categoryTextSelected: { color: colors.primary },
  description: { minHeight: 96, textAlignVertical: 'top' },
  photos: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  photo: { width: PHOTO_SIZE, height: PHOTO_SIZE, borderRadius: radius.sm, backgroundColor: colors.surface },
  removeBadge: {
    position: 'absolute',
    top: 4,
    right: 4,
    width: 24,
    height: 24,
    borderRadius: 12,
    overflow: 'hidden',
    textAlign: 'center',
    lineHeight: 24,
    color: colors.onPrimary,
    backgroundColor: 'rgba(0,0,0,0.6)',
    fontWeight: '700',
  },
  photoButtons: { flexDirection: 'row', gap: spacing.sm },
});
