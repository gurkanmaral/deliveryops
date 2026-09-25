import { env } from '@/config/env';
import { notificationsApi } from '@/shared/api';
import Constants from 'expo-constants';
import * as Device from 'expo-device';
import * as Notifications from 'expo-notifications';
import { router } from 'expo-router';
import { useEffect } from 'react';
import { Platform } from 'react-native';
import { useQueryClient } from '@tanstack/react-query';
import { orderKeys } from '../orders/order-api';
import { clearStoredPushToken, getStoredPushToken, savePushToken } from '../auth/session-storage';

Notifications.setNotificationHandler({
  handleNotification: async () => ({
    shouldPlaySound: true,
    shouldSetBadge: false,
    shouldShowBanner: true,
    shouldShowList: true,
  }),
});

async function ensureAndroidChannel() {
  if (Platform.OS !== 'android') return;
  await Notifications.setNotificationChannelAsync('orders', {
    name: 'Siparişler',
    description: 'Yeni ve atanan paket bildirimleri',
    importance: Notifications.AndroidImportance.HIGH,
    vibrationPattern: [0, 250, 150, 250],
    lightColor: '#5B5CE2',
  });
}

function getProjectId() {
  return env.easProjectId ?? Constants.easConfig?.projectId ?? Constants.expoConfig?.extra?.eas?.projectId;
}

let registrationPromise: Promise<string> | null = null;

async function registerTokenWithBackend(token: string) {
  await notificationsApi('/api/v1/devices', {
    method: 'POST',
    body: JSON.stringify({ expoPushToken: token, platform: Platform.OS, deviceName: Device.modelName }),
  });
  const previousToken = await getStoredPushToken();
  await savePushToken(token);
  if (previousToken && previousToken !== token) {
    await notificationsApi('/api/v1/devices', {
      method: 'DELETE',
      body: JSON.stringify({ expoPushToken: previousToken }),
    }).catch(() => undefined);
  }
  return token;
}

export async function registerPushNotifications(requestPermission = true) {
  if (registrationPromise) return registrationPromise;
  registrationPromise = registerPushNotificationsCore(requestPermission).finally(() => { registrationPromise = null; });
  return registrationPromise;
}

async function registerPushNotificationsCore(requestPermission: boolean) {
  if (Platform.OS === 'web') throw new Error('Push bildirimleri iOS veya Android cihazda etkinleştirilebilir.');
  if (!Device.isDevice) throw new Error('Push bildirimlerini test etmek için fiziksel cihaz kullanın.');
  await ensureAndroidChannel();
  let permission = await Notifications.getPermissionsAsync();
  if (permission.status !== 'granted' && requestPermission) permission = await Notifications.requestPermissionsAsync();
  if (permission.status !== 'granted') throw new Error('Bildirim izni verilmedi.');
  const projectId = getProjectId();
  if (!projectId) throw new Error('EAS project ID tanımlı değil. EXPO_PUBLIC_EAS_PROJECT_ID değerini ekleyin.');
  const token = (await Notifications.getExpoPushTokenAsync({ projectId })).data;
  return registerTokenWithBackend(token);
}

export async function unregisterPushNotifications() {
  const token = await getStoredPushToken();
  try {
    if (token) {
      await notificationsApi('/api/v1/devices', {
        method: 'DELETE',
        body: JSON.stringify({ expoPushToken: token }),
      });
    }
  } finally {
    await clearStoredPushToken();
    if (Platform.OS !== 'web') await Notifications.unregisterForNotificationsAsync().catch(() => undefined);
  }
}

function openNotification(notification: Notifications.Notification) {
  const url = notification.request.content.data?.url;
  const orderId = notification.request.content.data?.orderId;
  const params = typeof orderId === 'string' ? { orderId } : undefined;
  if (url === '/orders') router.push({ pathname: '/orders', params });
  if (url === '/available') router.push({ pathname: '/available', params });
}

export async function scheduleTestOrderNotification() {
  if (Platform.OS === 'web') throw new Error('Test bildirimi iOS veya Android üzerinde kullanılabilir.');
  const permission = await Notifications.requestPermissionsAsync();
  if (permission.status !== 'granted') throw new Error('Bildirim izni verilmedi.');
  await ensureAndroidChannel();
  await Notifications.scheduleNotificationAsync({
    content: {
      title: 'Test paketi atandı',
      body: 'Bildirime dokunarak Paketlerim ekranını açın.',
      data: { url: '/orders', orderId: 'development-test-order' },
      sound: 'default',
    },
    trigger: null,
  });
}

export function usePushNotifications() {
  const queryClient = useQueryClient();
  useEffect(() => {
    if (Platform.OS === 'web') return;
    const refreshOperations = () => {
      void queryClient.invalidateQueries({ queryKey: orderKeys.all });
      void queryClient.invalidateQueries({ queryKey: orderKeys.available });
      void queryClient.invalidateQueries({ queryKey: ['courier-profile'] });
    };
    void ensureAndroidChannel();
    Notifications.getPermissionsAsync().then(permission => {
      if (permission.status === 'granted') void registerPushNotifications(false).catch(() => undefined);
    });
    const initialResponse = Notifications.getLastNotificationResponse();
    if (initialResponse?.notification) {
      openNotification(initialResponse.notification);
      Notifications.clearLastNotificationResponse();
    }
    const receivedSubscription = Notifications.addNotificationReceivedListener(refreshOperations);
    const responseSubscription = Notifications.addNotificationResponseReceivedListener(response => {
      refreshOperations();
      openNotification(response.notification);
      Notifications.clearLastNotificationResponse();
    });
    const tokenSubscription = Notifications.addPushTokenListener(() => {
      void registerPushNotifications(false).catch(() => undefined);
    });
    return () => {
      receivedSubscription.remove();
      responseSubscription.remove();
      tokenSubscription.remove();
    };
  }, [queryClient]);
}
