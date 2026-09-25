import { Button, Card, IconBadge, PageHeader, Pill, Screen } from '@/components/ui';
import { useAuth } from '@/features/auth/auth-context';
import { flushLocationQueue, getQueuedLocationCount, isBackgroundLocationRunning, startBackgroundLocation, stopBackgroundLocation, submitLocation } from '@/features/location/location-task';
import { getAvailableOrders, getOrders, orderKeys } from '@/features/orders/order-api';
import { coreApi } from '@/shared/api';
import { availabilityLabels, deliveryStatusLabels } from '@/shared/labels';
import { OrderStatus } from '@/shared/types';
import { colors } from '@/theme/colors';
import { Ionicons } from '@expo/vector-icons';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { router } from 'expo-router';
import * as Location from 'expo-location';
import { useEffect, useState } from 'react';
import { Alert, AppState, Pressable, StyleSheet, Text, View } from 'react-native';

const activeStatuses = [OrderStatus.Assigned, OrderStatus.PickedUp, OrderStatus.OnTheWay, OrderStatus.DeliveryFailed];

export default function HomeScreen() {
  const { profile, refreshProfile } = useAuth();
  const queryClient = useQueryClient();
  const [locationState, setLocationState] = useState('Konum bekleniyor');
  const [backgroundRunning, setBackgroundRunning] = useState(false);
  const [queuedLocationCount, setQueuedLocationCount] = useState(0);
  const orders = useQuery({ queryKey: orderKeys.all, queryFn: getOrders });
  const available = useQuery({ queryKey: orderKeys.available, queryFn: getAvailableOrders });

  useEffect(() => {
    isBackgroundLocationRunning().then(setBackgroundRunning).catch(() => setBackgroundRunning(false));
    getQueuedLocationCount().then(setQueuedLocationCount).catch(() => setQueuedLocationCount(0));
  }, []);
  useEffect(() => {
    if (profile?.isShiftActive) void flushLocationQueue().then(setQueuedLocationCount).catch(() => undefined);
    const subscription = AppState.addEventListener('change', state => {
      if (state !== 'active' || !profile?.isShiftActive) return;
      flushLocationQueue().then(setQueuedLocationCount).catch(() => undefined);
    });
    return () => subscription.remove();
  }, [profile?.isShiftActive]);
  useEffect(() => {
    if (!profile?.isShiftActive) return;
    let subscription: Location.LocationSubscription | undefined;
    (async () => {
      const permission = await Location.requestForegroundPermissionsAsync();
      if (permission.status !== 'granted') { setLocationState('Konum izni gerekli'); return; }
      if (await isBackgroundLocationRunning()) { setLocationState('Arka planda takip ediliyor'); return; }
      subscription = await Location.watchPositionAsync({ accuracy: Location.Accuracy.Balanced, timeInterval: 15_000, distanceInterval: 25 }, location => {
        setLocationState(`Son sinyal ${new Date(location.timestamp).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })}`);
        void submitLocation(location).then(setQueuedLocationCount).catch(() => setLocationState('Konum kuyruğa alınamadı'));
      });
    })().catch(() => setLocationState('Konum başlatılamadı'));
    return () => subscription?.remove();
  }, [profile?.isShiftActive, backgroundRunning]);

  const shift = useMutation({
    mutationFn: async (start: boolean) => {
      if (!profile) return;
      await coreApi(`/api/v1/shifts/couriers/${profile.id}/${start ? 'start' : 'end'}`, { method: 'POST' });
      if (!start) await stopBackgroundLocation();
    },
    onSuccess: async () => { await refreshProfile(); await queryClient.invalidateQueries({ queryKey: orderKeys.all }); setBackgroundRunning(await isBackgroundLocationRunning()); },
    onError: (error: Error) => Alert.alert('Vardiya güncellenemedi', error.message),
  });

  const enableBackground = () => Alert.alert('Arka plan konumu', 'Aktif teslimat sırasında ekran kapalıyken de konumunuz operasyon paneline gönderilir. Telefon ayarlarında “Her zaman izin ver” seçeneğini kullanın.', [
    { text: 'Vazgeç', style: 'cancel' },
    { text: 'Devam et', onPress: () => startBackgroundLocation().then(() => { setBackgroundRunning(true); setLocationState('Arka planda takip ediliyor'); }).catch(error => Alert.alert('Konum başlatılamadı', error.message)) },
  ]);

  const myActiveCount = orders.data?.items.filter(x => x.courierId === profile?.id && activeStatuses.includes(x.status)).length ?? 0;
  const onShift = !!profile?.isShiftActive;
  const shiftSince = onShift && profile?.shiftStartedAtUtc ? new Date(profile.shiftStartedAtUtc).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' }) : null;
  const today = new Date().toLocaleDateString('tr-TR', { weekday: 'long', day: 'numeric', month: 'long' });
  return <Screen>
    <PageHeader eyebrow={today} title={`Merhaba, ${profile?.firstName ?? ''}`} />

    <View style={[styles.hero, onShift && styles.heroActive]}>
      <View style={styles.heroTop}>
        <View style={[styles.livePill, onShift && styles.livePillActive]}><View style={[styles.liveDot, onShift && styles.liveDotActive]} /><Text style={[styles.liveText, onShift && styles.liveTextActive]}>{onShift ? 'Vardiyada' : 'Çevrimdışı'}</Text></View>
        <Ionicons name={onShift ? 'radio' : 'moon-outline'} size={20} color={onShift ? colors.primary : colors.textFaint} />
      </View>
      <Text style={styles.heroTitle}>{onShift ? 'Paket almaya hazırsın' : 'Vardiyan kapalı'}</Text>
      <Text style={styles.heroCopy}>{shiftSince ? `${shiftSince} itibarıyla çalışıyorsun` : 'Paket almak ve konum paylaşmak için vardiyanı başlat.'}</Text>
      <Button size="lg" title={onShift ? 'Vardiyayı bitir' : 'Vardiyayı başlat'} variant={onShift ? 'secondary' : 'primary'} loading={shift.isPending} onPress={() => shift.mutate(!onShift)}
        icon={<Ionicons name={onShift ? 'stop-circle-outline' : 'play'} size={18} color={onShift ? colors.text : colors.white} />} />
    </View>

    <View style={styles.stats}>
      <Pressable accessibilityRole="button" onPress={() => router.navigate('/orders')} style={({ pressed }) => [styles.stat, pressed && styles.pressed]}>
        <View style={styles.statTop}><Text style={styles.statLabel}>Aktif paket</Text><Ionicons name="bag-handle" size={16} color={colors.primary} /></View>
        <Text style={styles.statValue}>{myActiveCount}</Text>
        <Text style={styles.statLink}>Paketlerim ›</Text>
      </Pressable>
      <Pressable accessibilityRole="button" onPress={() => router.navigate('/available')} style={({ pressed }) => [styles.stat, pressed && styles.pressed]}>
        <View style={styles.statTop}><Text style={styles.statLabel}>Havuzda</Text><Ionicons name="layers" size={16} color={colors.textMuted} /></View>
        <Text style={styles.statValue}>{available.data?.totalCount ?? 0}</Text>
        <Text style={styles.statLink}>Paket havuzu ›</Text>
      </Pressable>
    </View>

    <Card>
      <View style={styles.row}>
        <IconBadge tone={backgroundRunning ? 'success' : onShift ? 'primary' : 'neutral'}><Ionicons name="navigate" size={19} color={backgroundRunning ? colors.success : onShift ? colors.primary : colors.textMuted} /></IconBadge>
        <View style={styles.flex}><Text style={styles.cardTitle}>Canlı konum</Text><Text style={styles.muted}>{onShift ? locationState : 'Vardiya kapalı'}</Text></View>
        {backgroundRunning ? <Pill label="Arka plan" tone="success" /> : null}
      </View>
      {onShift && !backgroundRunning ? <Button title="Arka plan takibini etkinleştir" variant="secondary" onPress={enableBackground} icon={<Ionicons name="shield-checkmark-outline" size={17} color={colors.text} />} /> : null}
      {backgroundRunning ? <Text style={styles.success}>Ekran kapalıyken de konum gönderiliyor.</Text> : null}
      {queuedLocationCount > 0 ? <View style={styles.notice}><Ionicons name="cloud-offline-outline" size={16} color={colors.warning} /><Text style={styles.warning}>{queuedLocationCount} konum çevrimdışı kuyrukta; bağlantı gelince otomatik gönderilecek.</Text></View> : null}
    </Card>

    <Card style={styles.listCard}>
      <DetailRow icon="person-outline" label="Uygunluk" value={profile ? availabilityLabels[profile.availability] : '-'} />
      <DetailRow icon="bicycle-outline" label="Teslimat" value={profile ? deliveryStatusLabels[profile.deliveryStatus] : '-'} last />
    </Card>
  </Screen>;
}

function DetailRow({ icon, label, value, last }: { icon: keyof typeof Ionicons.glyphMap; label: string; value: string; last?: boolean }) {
  return <View style={[styles.detailRow, !last && styles.detailRowBorder]}>
    <Ionicons name={icon} size={18} color={colors.textMuted} />
    <Text style={styles.detailLabel}>{label}</Text>
    <Text style={styles.detailValue}>{value}</Text>
  </View>;
}

const styles = StyleSheet.create({
  hero: { backgroundColor: colors.ink, borderRadius: 22, padding: 18, gap: 8 },
  heroActive: { backgroundColor: colors.black },
  heroTop: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', marginBottom: 6 },
  livePill: { flexDirection: 'row', alignItems: 'center', gap: 6, paddingHorizontal: 10, paddingVertical: 5, borderRadius: 999, backgroundColor: colors.inkSoft },
  livePillActive: { backgroundColor: 'rgba(240, 82, 26, 0.16)' },
  liveDot: { width: 7, height: 7, borderRadius: 4, backgroundColor: colors.textFaint },
  liveDotActive: { backgroundColor: colors.primary },
  liveText: { color: colors.textFaint, fontSize: 12, fontWeight: '600' },
  liveTextActive: { color: '#FF9A6E' },
  heroTitle: { color: colors.white, fontSize: 22, lineHeight: 28, fontWeight: '700', letterSpacing: -0.5 },
  heroCopy: { color: colors.textFaint, fontSize: 14, lineHeight: 20, marginBottom: 10 },
  stats: { flexDirection: 'row', gap: 12 },
  stat: { flex: 1, backgroundColor: colors.surface, borderRadius: 18, borderWidth: StyleSheet.hairlineWidth, borderColor: colors.borderStrong, padding: 14, gap: 4 },
  statTop: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  statLabel: { color: colors.textMuted, fontSize: 13, fontWeight: '500' },
  statValue: { fontSize: 34, lineHeight: 40, fontWeight: '700', color: colors.text, letterSpacing: -1, fontVariant: ['tabular-nums'] },
  statLink: { color: colors.primaryDark, fontSize: 13, fontWeight: '600' },
  pressed: { opacity: 0.85 },
  row: { flexDirection: 'row', alignItems: 'center', gap: 12 }, flex: { flex: 1 },
  cardTitle: { color: colors.text, fontSize: 16, fontWeight: '600' },
  muted: { color: colors.textMuted, fontSize: 13, lineHeight: 18, marginTop: 1 },
  success: { color: colors.success, fontWeight: '500', fontSize: 13 },
  notice: { flexDirection: 'row', gap: 8, padding: 10, borderRadius: 12, backgroundColor: colors.warningSoft },
  warning: { flex: 1, color: colors.warning, fontWeight: '500', fontSize: 13, lineHeight: 18 },
  listCard: { paddingVertical: 4, gap: 0 },
  detailRow: { flexDirection: 'row', alignItems: 'center', gap: 10, paddingVertical: 13 },
  detailRowBorder: { borderBottomWidth: StyleSheet.hairlineWidth, borderBottomColor: colors.borderStrong },
  detailLabel: { flex: 1, color: colors.textSecondary, fontSize: 15 },
  detailValue: { color: colors.text, fontSize: 15, fontWeight: '600' },
});
