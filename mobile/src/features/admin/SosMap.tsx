import { memo } from 'react';
import { Platform, StyleSheet } from 'react-native';
import MapView, { Marker, PROVIDER_GOOGLE, type Region as MapRegion } from 'react-native-maps';

import type { SosMapPoint, SosStatus } from '@/api/types';

import type { Region } from './logic';

/**
 * Pin colours. Google Maps on Android tints the default pin by hue only, so these are pure hues;
 * the callout text always states the status too.
 */
export const PIN_COLORS: Record<SosStatus, string> = {
  Escalated: '#FF0000',
  Pending: '#FFA500',
  Approved: '#FF00FF',
  Resolved: '#00C000',
  Rejected: '#0000FF',
  Cancelled: '#0000FF',
  Expired: '#0000FF',
};

export interface SosMapProps {
  initialRegion: Region;
  points: SosMapPoint[];
  describe: (point: SosMapPoint) => { title: string; description: string };
  onRegionChange: (region: Region) => void;
  onOpen: (id: string) => void;
}

/** Google Maps on both platforms (the API key comes from app.config.ts). */
export const SosMap = memo(function SosMap({ initialRegion, points, describe, onRegionChange, onOpen }: SosMapProps) {
  return (
    <MapView
      style={StyleSheet.absoluteFill}
      provider={PROVIDER_GOOGLE}
      initialRegion={initialRegion}
      onMapReady={() => onRegionChange(initialRegion)}
      onRegionChangeComplete={(region: MapRegion) => onRegionChange(region)}
      showsUserLocation={false}
      toolbarEnabled={false}
      rotateEnabled={false}
      pitchEnabled={false}
      loadingEnabled={Platform.OS === 'android'}
    >
      {points.map((point) => {
        const text = describe(point);
        return (
          <Marker
            key={point.id}
            identifier={point.id}
            coordinate={{ latitude: point.latitude, longitude: point.longitude }}
            pinColor={PIN_COLORS[point.status]}
            title={text.title}
            description={text.description}
            onCalloutPress={() => onOpen(point.id)}
            tracksViewChanges={false}
          />
        );
      })}
    </MapView>
  );
});
