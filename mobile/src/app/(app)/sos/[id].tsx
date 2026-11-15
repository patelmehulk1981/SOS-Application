import { useQueryClient } from '@tanstack/react-query';
import type { TFunction } from 'i18next';
import { Image } from 'expo-image';
import { useLocalSearchParams } from 'expo-router';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ActivityIndicator, Linking, Modal, Pressable, RefreshControl, ScrollView, StyleSheet, Text, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { sosApi } from '@/api/endpoints';
import { isApiError } from '@/api/errors';
import { MAX_REVIEW_REMARKS, MAX_SOS_PHOTOS, type SosDetail, type SosPhoto } from '@/api/types';
import { classifyReviewError } from '@/features/admin/logic';
import { HelpPanel } from '@/features/alerts/HelpPanel';
import { RespondersSection } from '@/features/alerts/RespondersSection';
import { adminKeys, useApproveSos, useRejectSos } from '@/features/admin/queries';
import { deletePhotos, pickPhotos, toUploadFile } from '@/features/sos/photos';
import { isOpenStatus, sosKeys, useCancelSos, useResolveSos, useSosDetail } from '@/features/sos/queries';
import { useSosOutbox } from '@/features/sos/sosOutbox';
import { StatusBadge } from '@/features/sos/StatusBadge';
import { formatDateTime, roundAccuracy } from '@/lib/format';
import { formatPhone } from '@/lib/phone';
import { Banner } from '@/ui/Banner';
import { Button } from '@/ui/Button';
import { describeError, type ErrorDescription } from '@/ui/describeError';
import { EmergencyCallBar, callNumber } from '@/ui/EmergencyCallBar';
import { InfoRow, Section } from '@/ui/Rows';
import { TextDialog } from '@/ui/TextDialog';
import { colors, radius, spacing, typography } from '@/ui/theme';

type Dialog = 'cancel' | 'resolve' | 'approve' | 'reject';

export default function SosDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { t } = useTranslation();
  const query = useSosDetail(id);
  const cancel = useCancelSos();
  const resolve = useResolveSos();
  const approve = useApproveSos();
  const reject = useRejectSos();
  const client = useQueryClient();
  const outboxPending = useSosOutbox((s) => s.pending);

  const [dialog, setDialog] = useState<Dialog | null>(null);
  const [dialogError, setDialogError] = useState<string | null>(null);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<ErrorDescription | null>(null);
  const [notice, setNotice] = useState<{ tone: 'success' | 'info'; message: string } | null>(null);
  const [viewing, setViewing] = useState<SosPhoto | null>(null);

  if (query.isPending) {
    return (
      <View style={styles.center}>
        <ActivityIndicator size="large" color={colors.primary} />
      </View>
    );
  }

  if (query.isError || !query.data) {
    const notFound = isApiError(query.error, 'sos.not_found') || isApiError(query.error, 'not_found');
    return (
      <View style={[styles.center, styles.padded]}>
        <Banner tone="error" message={notFound ? t('sos.detail.notFound') : describeError(query.error, t).message} />
        {!notFound ? <Button variant="secondary" title={t('common.retry')} onPress={() => void query.refetch()} /> : null}
      </View>
    );
  }

  const sos: SosDetail = query.data;
  const open = isOpenStatus(sos.status);
  const isRequester = sos.viewerRole === 'Requester';
  const canCancel = isRequester && open;
  const canResolve = sos.status === 'Approved' && sos.viewerRole !== 'Recipient';
  const photosUploadingInBackground = outboxPending?.sosId === sos.id && outboxPending.photoUris.length > 0;
  const canAddPhotos = isRequester && open && sos.photos.length < MAX_SOS_PHOTOS && !photosUploadingInBackground;
  // The API decides who may review (assigned Sub-Admin or the Master Admin); the Admin view is only served to them.
  const canReview = sos.viewerRole === 'Admin' && (sos.status === 'Pending' || sos.status === 'Escalated');
  const accuracy = roundAccuracy(sos.location.accuracyMeters);

  const addPhotos = async () => {
    setError(null);
    const result = await pickPhotos('library', MAX_SOS_PHOTOS - sos.photos.length);
    if (result.kind !== 'picked') return;
    const uris = result.photos.map((p) => p.uri);
    setUploading(true);
    try {
      const updated = await sosApi.uploadPhotos(sos.id, uris.map(toUploadFile));
      client.setQueryData(sosKeys.detail(sos.id), updated);
    } catch (e) {
      setError(describeError(e, t));
    } finally {
      deletePhotos(uris);
      setUploading(false);
    }
  };

  const confirmDialog = (text: string) => {
    setDialogError(null);
    setNotice(null);
    const onError = (e: unknown) => setDialogError(describeError(e, t).message);
    const onSuccess = () => setDialog(null);
    if (dialog === 'cancel') cancel.mutate({ id: sos.id, reason: text || undefined }, { onSuccess, onError });
    if (dialog === 'resolve') resolve.mutate({ id: sos.id, remarks: text || undefined }, { onSuccess, onError });
    if (dialog === 'approve' || dialog === 'reject') {
      const onReviewError = (e: unknown) => {
        const failure = classifyReviewError(e);
        if (failure === 'other') {
          onError(e);
          return;
        }
        // Someone else (another admin, or the escalation job) got there first: show the current state.
        setDialog(null);
        setNotice({ tone: 'info', message: t(`admin.review.${failure}`) });
        void query.refetch();
        void client.invalidateQueries({ queryKey: adminKeys.sos() });
      };
      const onReviewed = () => {
        setDialog(null);
        setNotice({ tone: 'success', message: t(dialog === 'approve' ? 'admin.review.approved' : 'admin.review.rejected') });
      };
      if (dialog === 'approve') approve.mutate({ id: sos.id, remarks: text || undefined }, { onSuccess: onReviewed, onError: onReviewError });
      else reject.mutate({ id: sos.id, remarks: text }, { onSuccess: onReviewed, onError: onReviewError });
    }
  };

  const dialogText = dialog ? dialogStrings(dialog, t) : null;

  return (
    <SafeAreaView style={styles.safe} edges={['bottom', 'left', 'right']}>
      <ScrollView
        contentContainerStyle={styles.content}
        contentInsetAdjustmentBehavior="automatic"
        refreshControl={<RefreshControl refreshing={query.isRefetching} onRefresh={() => void query.refetch()} />}
      >
        <View style={styles.header}>
          <Text style={typography.title}>{t(`sos.category.${sos.category}`)}</Text>
          <StatusBadge status={sos.status} />
          {/* The status help is written for the requester ("mark it resolved when you are safe"). */}
          {sos.viewerRole !== 'Recipient' ? <Text style={typography.body}>{t(`sos.statusHelp.${sos.status}`)}</Text> : null}
          {sos.isAutoApproved ? <Text style={typography.small}>{t('sos.detail.autoApproved')}</Text> : null}
          {sos.recipientCount ? <Text style={typography.small}>{t('sos.detail.alerted', { count: sos.recipientCount })}</Text> : null}
        </View>

        {notice ? <Banner tone={notice.tone} message={notice.message} /> : null}

        {sos.viewerRole === 'Recipient' && sos.status === 'Approved' ? <HelpPanel sos={sos} /> : null}

        {canReview ? (
          <View style={styles.review}>
            <Text style={typography.body}>{t('admin.review.needsReview')}</Text>
            <Button title={t('admin.review.approve')} onPress={() => setDialog('approve')} />
            <Button variant="secondary" title={t('admin.review.reject')} onPress={() => setDialog('reject')} />
          </View>
        ) : null}

        {sos.reviewRemarks ? <Banner tone={sos.status === 'Rejected' ? 'error' : 'info'} message={sos.reviewRemarks} detail={t('sos.detail.remarks')} /> : null}

        <Section>
          <InfoRow label={t('sos.detail.reference')} value={sos.referenceNo} />
          <InfoRow label={t('sos.detail.raised')} value={formatDateTime(sos.createdAt)} />
          <InfoRow label={t('sos.detail.address')} value={sos.address ?? t('sos.detail.addressPending')} />
        </Section>
        {accuracy !== undefined ? <Text style={typography.small}>{t('sos.detail.accuracy', { accuracy })}</Text> : null}
        <Button variant="secondary" title={t('sos.detail.openMap')} onPress={() => void Linking.openURL(sos.location.mapsUrl)} />

        {sos.responders ? <RespondersSection responders={sos.responders} /> : null}

        {sos.viewerRole === 'Admin' ? <RequesterContact sos={sos} /> : null}

        {sos.description ? (
          <Section title={t('sos.detail.description')}>
            <Text style={[typography.body, styles.paragraph]}>{sos.description}</Text>
          </Section>
        ) : null}

        {sos.photos.length > 0 || canAddPhotos || photosUploadingInBackground ? (
          <View style={styles.group}>
            <Text style={styles.sectionTitle}>{t('sos.detail.photos')}</Text>
            <View style={styles.photos}>
              {sos.photos.map((photo) => (
                <Pressable key={photo.id} accessibilityRole="imagebutton" onPress={() => setViewing(photo)}>
                  {/* cacheKey = photo id: the signed URL changes every few minutes, the image does not. */}
                  <Image source={{ uri: photo.url, cacheKey: photo.id }} style={styles.photo} contentFit="cover" transition={150} />
                </Pressable>
              ))}
            </View>
            {photosUploadingInBackground || uploading ? <Text style={typography.small}>{t('sos.detail.photosUploading')}</Text> : null}
            {canAddPhotos && !uploading ? <Button variant="secondary" title={t('sos.detail.addPhotos')} onPress={() => void addPhotos()} /> : null}
          </View>
        ) : null}

        {sos.timeline && sos.timeline.length > 0 ? (
          <Section title={t('sos.detail.timeline')}>
            {sos.timeline.map((entry, index) => (
              <View key={`${entry.changedAt}-${index}`} style={styles.timelineRow}>
                <Text style={typography.label}>
                  {t(`sos.status.${entry.to}`)}
                  {entry.bySystem ? ` · ${t('sos.detail.bySystem')}` : ''}
                </Text>
                <Text style={typography.small}>{formatDateTime(entry.changedAt)}</Text>
                {entry.remarks ? <Text style={typography.body}>{entry.remarks}</Text> : null}
              </View>
            ))}
          </Section>
        ) : null}

        {error ? <Banner tone="error" message={error.message} detail={error.reference} /> : null}

        {canResolve ? <Button title={t('sos.detail.resolve')} onPress={() => setDialog('resolve')} /> : null}
        {canCancel ? <Button variant="secondary" title={t('sos.detail.cancel')} onPress={() => setDialog('cancel')} /> : null}
        <EmergencyCallBar />
      </ScrollView>

      <TextDialog
        visible={dialogText !== null}
        title={dialogText?.title ?? ''}
        label={dialogText?.label ?? ''}
        placeholder={dialogText?.placeholder}
        confirmTitle={dialogText?.confirm ?? ''}
        cancelTitle={dialogText?.dismiss ?? ''}
        required={dialog === 'reject'}
        maxLength={MAX_REVIEW_REMARKS}
        busy={cancel.isPending || resolve.isPending || approve.isPending || reject.isPending}
        error={dialogError}
        onConfirm={confirmDialog}
        onCancel={() => {
          setDialog(null);
          setDialogError(null);
        }}
      />

      <Modal visible={viewing !== null} transparent={false} animationType="fade" onRequestClose={() => setViewing(null)}>
        <Pressable style={styles.viewer} onPress={() => setViewing(null)} accessibilityRole="button">
          {viewing ? (
            <Image source={{ uri: viewing.url, cacheKey: viewing.id }} style={StyleSheet.absoluteFill} contentFit="contain" />
          ) : null}
        </Pressable>
      </Modal>
    </SafeAreaView>
  );
}

function dialogStrings(dialog: Dialog, t: TFunction) {
  switch (dialog) {
    case 'cancel':
      return {
        title: t('sos.detail.cancelTitle'),
        label: t('sos.detail.cancelReasonLabel'),
        placeholder: t('sos.detail.cancelReasonPlaceholder'),
        confirm: t('sos.detail.cancelConfirm'),
        dismiss: t('sos.detail.keep'),
      };
    case 'resolve':
      return {
        title: t('sos.detail.resolveTitle'),
        label: t('sos.detail.resolveRemarksLabel'),
        placeholder: t('sos.detail.resolveRemarksPlaceholder'),
        confirm: t('sos.detail.resolveConfirm'),
        dismiss: t('sos.detail.keep'),
      };
    case 'approve':
      return {
        title: t('admin.review.approveTitle'),
        label: t('admin.review.approveLabel'),
        placeholder: t('admin.review.approvePlaceholder'),
        confirm: t('admin.review.approveConfirm'),
        dismiss: t('admin.review.back'),
      };
    case 'reject':
      return {
        title: t('admin.review.rejectTitle'),
        label: t('admin.review.rejectLabel'),
        placeholder: t('admin.review.rejectPlaceholder'),
        confirm: t('admin.review.rejectConfirm'),
        dismiss: t('admin.review.back'),
      };
  }
}

/** Admin view only: who to call (docs/03d: phone, blood group, emergency contact). */
function RequesterContact({ sos }: { sos: SosDetail }) {
  const { t } = useTranslation();
  const r = sos.requester;
  const phone = r.phoneNumber;
  return (
    <Section title={r.fullName ?? r.firstName}>
      {phone ? (
        <Button
          variant="secondary"
          title={formatPhone(phone)}
          onPress={() => void callNumber(phone, t('emergency.callFailed', { number: phone }))}
        />
      ) : null}
      {r.bloodGroup ? <InfoRow label={t('profileEdit.bloodGroup')} value={r.bloodGroup} /> : null}
      {r.emergencyContactName || r.emergencyContactPhone ? (
        <InfoRow
          label={t('profileEdit.emergencyName')}
          value={[r.emergencyContactName, r.emergencyContactPhone ? formatPhone(r.emergencyContactPhone) : undefined].filter(Boolean).join(' · ')}
        />
      ) : null}
    </Section>
  );
}

const PHOTO_SIZE = 104;

const styles = StyleSheet.create({
  safe: { flex: 1, backgroundColor: colors.background },
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: spacing.md, backgroundColor: colors.background },
  padded: { padding: spacing.lg },
  content: { padding: spacing.lg, gap: spacing.lg },
  header: { gap: spacing.sm },
  group: { gap: spacing.sm },
  review: { gap: spacing.sm, padding: spacing.md, borderRadius: radius.lg, borderWidth: 2, borderColor: colors.primary },
  sectionTitle: { ...typography.label, color: colors.textMuted, textTransform: 'uppercase', fontSize: 13 },
  paragraph: { padding: spacing.md },
  photos: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  photo: { width: PHOTO_SIZE, height: PHOTO_SIZE, borderRadius: radius.sm, backgroundColor: colors.surface },
  timelineRow: { paddingHorizontal: spacing.md, paddingVertical: spacing.sm, gap: 2 },
  viewer: { flex: 1, backgroundColor: '#000' },
});
