import * as SecureStore from 'expo-secure-store';
import { Platform } from 'react-native';

const ACCESS_TOKEN_KEY = 'deliveryops.access-token';
const REFRESH_TOKEN_KEY = 'deliveryops.refresh-token';
const COURIER_ID_KEY = 'deliveryops.courier-id';
const PUSH_TOKEN_KEY = 'deliveryops.push-token';

const webStorage = {
  get: async (key: string) => (typeof localStorage === 'undefined' ? null : localStorage.getItem(key)),
  set: async (key: string, value: string) => localStorage.setItem(key, value),
  remove: async (key: string) => localStorage.removeItem(key),
};

const secureStoreOptions: SecureStore.SecureStoreOptions = {
  // Tokens are usable by the native background-location task after the first device unlock,
  // but are not migrated into backups or onto a different device.
  keychainAccessible: SecureStore.AFTER_FIRST_UNLOCK_THIS_DEVICE_ONLY,
};

const storage = Platform.OS === 'web'
  ? webStorage
  : {
      get: (key: string) => SecureStore.getItemAsync(key),
      set: (key: string, value: string) => SecureStore.setItemAsync(key, value, secureStoreOptions),
      remove: (key: string) => SecureStore.deleteItemAsync(key),
    };

export const getAccessToken = () => storage.get(ACCESS_TOKEN_KEY);
export const getRefreshToken = () => storage.get(REFRESH_TOKEN_KEY);
export const getCourierId = () => storage.get(COURIER_ID_KEY);
export const getStoredPushToken = () => storage.get(PUSH_TOKEN_KEY);

export async function saveTokens(accessToken: string, refreshToken: string) {
  await Promise.all([
    storage.set(ACCESS_TOKEN_KEY, accessToken),
    storage.set(REFRESH_TOKEN_KEY, refreshToken),
  ]);
}

export const saveCourierId = (courierId: string) => storage.set(COURIER_ID_KEY, courierId);
export const savePushToken = (pushToken: string) => storage.set(PUSH_TOKEN_KEY, pushToken);
export const clearStoredPushToken = () => storage.remove(PUSH_TOKEN_KEY);

export async function clearSessionStorage() {
  await Promise.all([
    storage.remove(ACCESS_TOKEN_KEY),
    storage.remove(REFRESH_TOKEN_KEY),
    storage.remove(COURIER_ID_KEY),
    storage.remove(PUSH_TOKEN_KEY),
  ]);
}
