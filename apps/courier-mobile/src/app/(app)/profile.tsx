import { Button, Card, Pill, Screen } from '@/components/ui';
import { useAuth } from '@/features/auth/auth-context';
import { stopBackgroundLocation } from '@/features/location/location-task';
import { registerPushNotifications, scheduleTestOrderNotification, unregisterPushNotifications } from '@/features/notifications/push-notifications';
import { getShiftSummary, orderKeys } from '@/features/orders/order-api';
import { availabilityLabels, deliveryStatusLabels } from '@/shared/labels';
import { useQuery } from '@tanstack/react-query';
import { colors } from '@/theme/colors';
import { Ionicons } from '@expo/vector-icons';
import { Alert, Platform, Pressable, StyleSheet, Text, View } from 'react-native';

export default function ProfileScreen() {
  const { profile, signOut } = useAuth();
  const summary = useQuery({ queryKey: orderKeys.shiftSummary, queryFn: getShiftSummary, enabled: !!profile?.isShiftActive });
  const money = (value: number | undefined) => (value ?? 0).toLocaleString('tr-TR', { style: 'currency', currency: summary.data?.currency ?? 'TRY' });
  const logout = async () => { await unregisterPushNotifications().catch(() => undefined); await stopBackgroundLocation().catch(() => undefined); await signOut(); };
  const enableNotifications = () => registerPushNotifications(true)
    .then(() => Alert.alert('Bildirimler açık', 'Yeni ve atanan paket bildirimlerini alacaksın.'))
    .catch(error => Alert.alert('Bildirimler açılamadı', error.message));
  const testNotification = () => scheduleTestOrderNotification()
    .catch(error => Alert.alert('Test bildirimi oluşturulamadı', error.message));
  const initials = `${profile?.firstName?.[0] ?? ''}${profile?.lastName?.[0] ?? ''}`.toLocaleUpperCase('tr-TR');
  return <Screen>
    <View style={styles.identity}>
      <View style={styles.avatar}><Text style={styles.avatarText}>{initials || 'K'}</Text></View>
      <View style={styles.flex}>
        <Text style={styles.name}>{profile?.firstName} {profile?.lastName}</Text>
        <Text style={styles.phone}>{profile?.phoneNumber ?? '-'}</Text>
      </View>
      <Pill label={profile?.isShiftActive ? 'Vardiyada' : 'Kapalı'} tone={profile?.isShiftActive ? 'primary' : 'neutral'} />
    </View>

    <Text style={styles.section}>Operasyon</Text>
    <Card style={styles.list}>
      <Row icon="person-outline" label="Uygunluk" value={profile ? availabilityLabels[profile.availability] : '-'} />
      <Row icon="bicycle-outline" label="Teslimat durumu" value={profile ? deliveryStatusLabels[profile.deliveryStatus] : '-'} />
      <Row icon="time-outline" label="Vardiya" value={profile?.isShiftActive ? 'Aktif' : 'Kapalı'} last />
    </Card>

    {profile?.isShiftActive ? <>
      <Text style={styles.section}>Bu vardiya</Text>
      <Card style={styles.list}>
        <Row icon="checkmark-done-outline" label="Teslim edilen paket" value={String(summary.data?.deliveredCount ?? 0)} />
        <Row icon="cash-outline" label="Kasaya teslim edilecek nakit" value={money(summary.data?.cashCollected)} />
        <Row icon="card-outline" label="Kartla tahsil edilen" value={money(summary.data?.cardCollected)} last />
      </Card>
    </> : null}

    <Text style={styles.section}>Ayarlar</Text>
    <Card style={styles.list}>
      <ActionRow icon="notifications-outline" label="Paket bildirimlerini etkinleştir" onPress={enableNotifications} last={!(__DEV__ && Platform.OS !== 'web')} />
      {__DEV__ && Platform.OS !== 'web' ? <ActionRow icon="flask-outline" label="Test paket bildirimi gönder" onPress={testNotification} last /> : null}
    </Card>

    <View style={styles.privacy}>
      <Ionicons name="lock-closed-outline" size={16} color={colors.textMuted} />
      <Text style={styles.note}>Konum yalnızca operasyon ve aktif teslimat takibi için kullanılır. Vardiyayı bitirdiğinde veya çıkış yaptığında arka plan takibi durdurulur.</Text>
    </View>

    <Button title="Çıkış yap" variant="secondary" onPress={logout} icon={<Ionicons name="log-out-outline" size={18} color={colors.danger} />} />
    <Text style={styles.version}>DeliveryOps Kurye · v1.0.0</Text>
  </Screen>;
}

type IconName = keyof typeof Ionicons.glyphMap;
function Row({ icon, label, value, last }: { icon: IconName; label: string; value: string; last?: boolean }) {
  return <View style={[styles.row, !last && styles.rowBorder]}><Ionicons name={icon} size={18} color={colors.textMuted} /><Text style={styles.label}>{label}</Text><Text style={styles.value}>{value}</Text></View>;
}
function ActionRow({ icon, label, onPress, last }: { icon: IconName; label: string; onPress(): void; last?: boolean }) {
  return <Pressable accessibilityRole="button" onPress={onPress} style={({ pressed }) => [styles.row, !last && styles.rowBorder, pressed && styles.pressed]}>
    <Ionicons name={icon} size={18} color={colors.primary} /><Text style={styles.label}>{label}</Text><Ionicons name="chevron-forward" size={17} color={colors.textFaint} />
  </Pressable>;
}

const styles = StyleSheet.create({
  identity: { flexDirection: 'row', alignItems: 'center', gap: 14, paddingVertical: 8 },
  avatar: { width: 60, height: 60, borderRadius: 18, backgroundColor: colors.ink, alignItems: 'center', justifyContent: 'center' },
  avatarText: { color: colors.white, fontSize: 21, fontWeight: '700', letterSpacing: 0.5 },
  flex: { flex: 1 },
  name: { color: colors.text, fontSize: 22, fontWeight: '700', letterSpacing: -0.5 },
  phone: { color: colors.textMuted, fontSize: 14, marginTop: 2, fontVariant: ['tabular-nums'] },
  section: { color: colors.textMuted, fontSize: 12, fontWeight: '600', letterSpacing: 0.8, textTransform: 'uppercase', marginTop: 8, marginLeft: 4 },
  list: { paddingVertical: 2, gap: 0 },
  row: { flexDirection: 'row', alignItems: 'center', gap: 12, paddingVertical: 14, minHeight: 50 },
  rowBorder: { borderBottomWidth: StyleSheet.hairlineWidth, borderBottomColor: colors.borderStrong },
  pressed: { opacity: 0.6 },
  label: { flex: 1, color: colors.text, fontSize: 15 },
  value: { color: colors.textSecondary, fontSize: 15, fontWeight: '600' },
  privacy: { flexDirection: 'row', gap: 10, padding: 14, borderRadius: 16, backgroundColor: colors.surfaceMuted, marginTop: 4 },
  note: { flex: 1, color: colors.textMuted, fontSize: 13, lineHeight: 19 },
  version: { textAlign: 'center', color: colors.textFaint, fontSize: 12, marginTop: 4 },
});
