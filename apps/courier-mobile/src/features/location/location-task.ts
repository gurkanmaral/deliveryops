import { coreApi } from '@/shared/api';
import * as Location from 'expo-location';
import * as TaskManager from 'expo-task-manager';
import { getCourierId } from '../auth/session-storage';
import {
  clearEncryptedLocationQueue,
  readEncryptedLocationQueue,
  writeEncryptedLocationQueue,
} from './encrypted-location-storage';

export const BACKGROUND_LOCATION_TASK = 'deliveryops-background-location';
const MAX_QUEUED_LOCATIONS = 100;

export type LocationPayload = {
  latitude: number;
  longitude: number;
  accuracyMeters: number | null;
  speedMetersPerSecond: number | null;
  headingDegrees: number | null;
  recordedAtUtc: string;
};

type QueuedLocation = {
  courierId: string;
  payload: LocationPayload;
};

let queueWork: Promise<void> = Promise.resolve();

function withQueueLock<T>(operation: () => Promise<T>): Promise<T> {
  const result = queueWork.then(operation, operation);
  queueWork = result.then(() => undefined, () => undefined);
  return result;
}

function toPayload(location: Location.LocationObject): LocationPayload {
  return {
    latitude: location.coords.latitude,
    longitude: location.coords.longitude,
    accuracyMeters: location.coords.accuracy,
    speedMetersPerSecond: location.coords.speed,
    headingDegrees: location.coords.heading,
    recordedAtUtc: new Date(location.timestamp).toISOString(),
  };
}

async function readQueue(): Promise<QueuedLocation[]> {
  const value = await readEncryptedLocationQueue();
  if (!value) return [];
  try {
    const parsed = JSON.parse(value) as unknown;
    if (!Array.isArray(parsed)) return [];
    return parsed.filter((item): item is QueuedLocation => {
      if (!item || typeof item !== 'object') return false;
      const candidate = item as Partial<QueuedLocation>;
      return typeof candidate.courierId === 'string' && !!candidate.payload;
    });
  } catch {
    return [];
  }
}

async function writeQueue(queued: QueuedLocation[]) {
  if (!queued.length) {
    await writeEncryptedLocationQueue(null);
    return;
  }
  await writeEncryptedLocationQueue(JSON.stringify(queued.slice(-MAX_QUEUED_LOCATIONS)));
}

async function sendLocation(courierId: string, payload: LocationPayload) {
  await coreApi(`/api/v1/locations/couriers/${courierId}`, {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function submitLocation(location: Location.LocationObject) {
  const payload = toPayload(location);
  const courierId = await getCourierId();
  if (!courierId) return 0;
  return withQueueLock(async () => {
    const queued = (await readQueue()).filter(item => item.courierId === courierId);
    queued.push({ courierId, payload });
    await writeQueue(queued);
    return drainQueue(queued);
  });
}

export async function flushLocationQueue() {
  const courierId = await getCourierId();
  if (!courierId) return 0;
  return withQueueLock(async () => {
    const queued = (await readQueue()).filter(item => item.courierId === courierId);
    await writeQueue(queued);
    return drainQueue(queued);
  });
}

async function drainQueue(queued: QueuedLocation[]) {
  const remaining = [...queued];
  while (remaining.length) {
    try {
      await sendLocation(remaining[0].courierId, remaining[0].payload);
      remaining.shift();
      await writeQueue(remaining);
    } catch {
      await writeQueue(remaining);
      break;
    }
  }
  return remaining.length;
}

export async function getQueuedLocationCount() {
  const courierId = await getCourierId();
  if (!courierId) return 0;
  return withQueueLock(async () => (await readQueue()).filter(item => item.courierId === courierId).length);
}

export async function clearLocationQueue() {
  await withQueueLock(clearEncryptedLocationQueue);
}

TaskManager.defineTask<{ locations: Location.LocationObject[] }>(BACKGROUND_LOCATION_TASK, async ({ data, error }) => {
  if (error || !data?.locations?.length) return;
  for (const location of data.locations) {
    try { await submitLocation(location); } catch { /* A later update will retry persisted locations. */ }
  }
});

export async function startBackgroundLocation() {
  const foreground = await Location.requestForegroundPermissionsAsync();
  if (foreground.status !== 'granted') throw new Error('Konum izni verilmedi.');
  const background = await Location.requestBackgroundPermissionsAsync();
  if (background.status !== 'granted') throw new Error('Arka plan konum izni verilmedi.');
  if (await Location.hasStartedLocationUpdatesAsync(BACKGROUND_LOCATION_TASK)) return;
  await Location.startLocationUpdatesAsync(BACKGROUND_LOCATION_TASK, {
    accuracy: Location.Accuracy.Balanced,
    timeInterval: 15_000,
    distanceInterval: 25,
    deferredUpdatesInterval: 60_000,
    deferredUpdatesDistance: 50,
    pausesUpdatesAutomatically: false,
    showsBackgroundLocationIndicator: true,
    foregroundService: {
      notificationTitle: 'DeliveryOps vardiyası aktif',
      notificationBody: 'Teslimat konumunuz operasyon merkeziyle paylaşılıyor.',
      notificationColor: '#5B5CE2',
    },
  });
}

export async function stopBackgroundLocation() {
  if (await Location.hasStartedLocationUpdatesAsync(BACKGROUND_LOCATION_TASK)) {
    await Location.stopLocationUpdatesAsync(BACKGROUND_LOCATION_TASK);
  }
}

export async function isBackgroundLocationRunning() {
  return Location.hasStartedLocationUpdatesAsync(BACKGROUND_LOCATION_TASK);
}
