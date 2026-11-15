import { Directory, File, Paths } from 'expo-file-system';
import { ImageManipulator, SaveFormat } from 'expo-image-manipulator';
import * as ImagePicker from 'expo-image-picker';

import type { UploadFile } from '@/api/endpoints';

/** A photo prepared for upload, stored in the app's own documents folder until it has been sent. */
export interface LocalPhoto {
  uri: string;
  width: number;
  height: number;
}

/** The server keeps at most 2048 px and re-encodes anyway; resizing first saves upload time on slow networks. */
const MAX_DIMENSION = 2048;
const JPEG_QUALITY = 0.7;

export type PhotoSource = 'camera' | 'library';

export type PickResult =
  | { kind: 'picked'; photos: LocalPhoto[] }
  | { kind: 'cancelled' }
  | { kind: 'permissionDenied' };

function outboxDirectory(): Directory {
  const dir = new Directory(Paths.document, 'sos-photos');
  if (!dir.exists) dir.create({ intermediates: true, idempotent: true });
  return dir;
}

export async function pickPhotos(source: PhotoSource, remaining: number): Promise<PickResult> {
  if (remaining <= 0) return { kind: 'cancelled' };

  const options: ImagePicker.ImagePickerOptions = {
    mediaTypes: ['images'],
    quality: 1, // compressed once, below
    exif: false,
  };

  let result: ImagePicker.ImagePickerResult;
  if (source === 'camera') {
    const permission = await ImagePicker.requestCameraPermissionsAsync();
    if (!permission.granted) return { kind: 'permissionDenied' };
    result = await ImagePicker.launchCameraAsync(options);
  } else {
    // The system photo picker needs no media-library permission on Android 13+ and iOS 14+.
    result = await ImagePicker.launchImageLibraryAsync({ ...options, allowsMultipleSelection: true, selectionLimit: remaining });
  }

  if (result.canceled || result.assets.length === 0) return { kind: 'cancelled' };
  const photos = await Promise.all(result.assets.slice(0, remaining).map((asset) => preparePhoto(asset)));
  return { kind: 'picked', photos };
}

/** Downscale to at most 2048 px and re-encode as JPEG (also drops EXIF, e.g. GPS, before upload). */
async function preparePhoto(asset: ImagePicker.ImagePickerAsset): Promise<LocalPhoto> {
  const context = ImageManipulator.manipulate(asset.uri);
  const longest = Math.max(asset.width, asset.height);
  if (longest > MAX_DIMENSION) {
    context.resize(asset.width >= asset.height ? { width: MAX_DIMENSION } : { height: MAX_DIMENSION });
  }
  const image = await context.renderAsync();
  const saved = await image.saveAsync({ compress: JPEG_QUALITY, format: SaveFormat.JPEG });

  // Move out of the cache folder so an SOS waiting for a connection does not lose its photos.
  const source = new File(saved.uri);
  const target = new File(outboxDirectory(), `${Date.now()}-${Math.random().toString(36).slice(2, 8)}.jpg`);
  source.move(target);
  return { uri: target.uri, width: saved.width, height: saved.height };
}

export function toUploadFile(uri: string, index: number): UploadFile {
  return { uri, name: `photo-${index + 1}.jpg`, type: 'image/jpeg' };
}

export function deletePhotos(uris: readonly string[]): void {
  for (const uri of uris) {
    try {
      const file = new File(uri);
      if (file.exists) file.delete();
    } catch {
      // Already gone.
    }
  }
}
