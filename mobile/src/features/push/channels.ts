import { AndroidImportance, AndroidNotificationVisibility, setNotificationChannelAsync } from 'expo-notifications';
import { Platform } from 'react-native';

/**
 * Android notification channels. The ids must match the API (backend PushMessages.cs). Users can tune each
 * channel in system settings; importance cannot be raised by the app later, so it is set right the first time.
 * Lock-screen visibility is PRIVATE: the content shows only after unlocking unless the user allows it.
 */
const CHANNELS: { id: string; name: string; description: string; importance: AndroidImportance }[] = [
  {
    id: 'sos-alert',
    name: 'SOS near you',
    description: 'Someone within 5 km needs help.',
    importance: AndroidImportance.MAX,
  },
  {
    id: 'sos-admin',
    name: 'SOS to review',
    description: 'For community admins: SOS waiting for review or escalated.',
    importance: AndroidImportance.MAX,
  },
  {
    id: 'sos-status',
    name: 'SOS updates',
    description: 'Your SOS was approved or rejected, someone is coming, or an SOS no longer needs help.',
    importance: AndroidImportance.HIGH,
  },
];

export async function ensureNotificationChannels(): Promise<void> {
  if (Platform.OS !== 'android') return;
  await Promise.all(
    CHANNELS.map((channel) =>
      setNotificationChannelAsync(channel.id, {
        name: channel.name,
        description: channel.description,
        importance: channel.importance,
        sound: 'default',
        enableVibrate: true,
        vibrationPattern: channel.importance === AndroidImportance.MAX ? [0, 400, 200, 400, 200, 400] : [0, 250],
        lockscreenVisibility: AndroidNotificationVisibility.PRIVATE,
        showBadge: true,
        lightColor: '#B71C1C',
      }),
    ),
  );
}
