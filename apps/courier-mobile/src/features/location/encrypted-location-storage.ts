import AsyncStorage from '@react-native-async-storage/async-storage';
import {
  AESEncryptionKey,
  AESSealedData,
  aesDecryptAsync,
  aesEncryptAsync,
} from 'expo-crypto';
import * as SecureStore from 'expo-secure-store';
import { Platform } from 'react-native';

const QUEUE_KEY = 'deliveryops.location-queue';
const ENCRYPTION_KEY = 'deliveryops.location-queue-key.v1';
const ENVELOPE_PREFIX = 'aes-gcm:v1:';
const AAD = new TextEncoder().encode('deliveryops.location-queue:v1');

let webValue: string | null = null;

async function getEncryptionKey() {
  const encoded = await SecureStore.getItemAsync(ENCRYPTION_KEY);
  if (encoded) return AESEncryptionKey.import(encoded, 'base64');

  const key = await AESEncryptionKey.generate();
  await SecureStore.setItemAsync(ENCRYPTION_KEY, await key.encoded('base64'), {
    keychainAccessible: SecureStore.AFTER_FIRST_UNLOCK_THIS_DEVICE_ONLY,
  });
  return key;
}

export async function readEncryptedLocationQueue(): Promise<string | null> {
  if (Platform.OS === 'web') return webValue;

  const stored = await AsyncStorage.getItem(QUEUE_KEY);
  if (!stored) return null;

  // Existing installations are migrated on the next successful queue write.
  if (!stored.startsWith(ENVELOPE_PREFIX)) return stored;

  try {
    const key = await getEncryptionKey();
    const sealed = AESSealedData.fromCombined(stored.slice(ENVELOPE_PREFIX.length));
    const plaintext = await aesDecryptAsync(sealed, key, { additionalData: AAD });
    return new TextDecoder().decode(plaintext);
  } catch {
    // Authentication failures mean the ciphertext was modified or its device-bound key was lost.
    await AsyncStorage.removeItem(QUEUE_KEY);
    return null;
  }
}

export async function writeEncryptedLocationQueue(value: string | null) {
  if (Platform.OS === 'web') {
    // Background tracking is native-only; never persist precise locations in browser storage.
    webValue = value;
    return;
  }

  if (!value) {
    await AsyncStorage.removeItem(QUEUE_KEY);
    return;
  }

  const key = await getEncryptionKey();
  const sealed = await aesEncryptAsync(new TextEncoder().encode(value), key, { additionalData: AAD });
  await AsyncStorage.setItem(QUEUE_KEY, `${ENVELOPE_PREFIX}${await sealed.combined('base64')}`);
}

export async function clearEncryptedLocationQueue() {
  webValue = null;
  if (Platform.OS !== 'web') await AsyncStorage.removeItem(QUEUE_KEY);
}
