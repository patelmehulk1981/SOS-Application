import { useState } from 'react';
import { KeyboardAvoidingView, Modal, Platform, Pressable, StyleSheet, Text, View } from 'react-native';

import { Banner } from './Banner';
import { Button } from './Button';
import { TextField } from './TextField';
import { colors, radius, spacing, typography } from './theme';

export interface TextDialogProps {
  visible: boolean;
  title: string;
  label: string;
  placeholder?: string;
  confirmTitle: string;
  cancelTitle: string;
  maxLength?: number;
  /** Text must be non-empty to confirm. */
  required?: boolean;
  busy?: boolean;
  error?: string | null;
  onConfirm: (text: string) => void;
  onCancel: () => void;
}

/** Confirmation with a free-text field (cancel reason, resolve remarks, reject remarks). Works the same on both platforms. */
export function TextDialog(props: TextDialogProps) {
  const { visible, busy = false, onCancel } = props;
  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={busy ? undefined : onCancel} statusBarTranslucent>
      {/* Mounted only while open, so the text starts empty every time. */}
      {visible ? <DialogBody {...props} /> : null}
    </Modal>
  );
}

function DialogBody({
  title,
  label,
  placeholder,
  confirmTitle,
  cancelTitle,
  maxLength = 500,
  required = false,
  busy = false,
  error,
  onConfirm,
  onCancel,
}: TextDialogProps) {
  const [text, setText] = useState('');
  const trimmed = text.trim();

  return (
    <>
      <KeyboardAvoidingView style={styles.backdrop} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <Pressable style={StyleSheet.absoluteFill} onPress={busy ? undefined : onCancel} accessible={false} />
        <View style={styles.card} accessibilityViewIsModal>
          <Text style={typography.heading} accessibilityRole="header">
            {title}
          </Text>
          <TextField
            label={label}
            placeholder={placeholder}
            value={text}
            onChangeText={setText}
            multiline
            maxLength={maxLength}
            style={styles.input}
            autoFocus
          />
          {error ? <Banner tone="error" message={error} /> : null}
          <Button title={confirmTitle} onPress={() => onConfirm(trimmed)} loading={busy} disabled={required && trimmed === ''} />
          <Button variant="ghost" title={cancelTitle} onPress={onCancel} disabled={busy} />
        </View>
      </KeyboardAvoidingView>
    </>
  );
}

const styles = StyleSheet.create({
  backdrop: { flex: 1, justifyContent: 'center', padding: spacing.lg, backgroundColor: 'rgba(0,0,0,0.45)' },
  card: { backgroundColor: colors.background, borderRadius: radius.lg, padding: spacing.lg, gap: spacing.md },
  input: { minHeight: 88, textAlignVertical: 'top' },
});
