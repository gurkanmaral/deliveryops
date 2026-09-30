import { Button, Card, Pill } from '@/components/ui';
import { orderSourceLabels, orderStatusLabels } from '@/shared/labels';
import { OrderStatus, PaymentMethod, PaymentStatus, type Order } from '@/shared/types';
import { colors, type Tone } from '@/theme/colors';
import { Ionicons } from '@expo/vector-icons';
import * as Linking from 'expo-linking';
import { useState } from 'react';
import { Alert, Pressable, StyleSheet, Text, TextInput, View } from 'react-native';

const actionLabels: Partial<Record<OrderStatus, string>> = {
  [OrderStatus.PickedUp]: 'Paketi aldım', [OrderStatus.OnTheWay]: 'Teslimata başla',
  [OrderStatus.Delivered]: 'Teslim ettim', [OrderStatus.Returned]: 'İade edildi',
};

const actionIcons: Partial<Record<OrderStatus, keyof typeof Ionicons.glyphMap>> = {
  [OrderStatus.PickedUp]: 'cube', [OrderStatus.OnTheWay]: 'bicycle',
  [OrderStatus.Delivered]: 'checkmark-circle', [OrderStatus.Returned]: 'return-down-back',
};

const statusTones: Record<OrderStatus, Tone> = {
  [OrderStatus.New]: 'neutral', [OrderStatus.Confirmed]: 'neutral', [OrderStatus.WaitingForCourier]: 'warning',
  [OrderStatus.Assigned]: 'primary', [OrderStatus.PickedUp]: 'info', [OrderStatus.OnTheWay]: 'info',
  [OrderStatus.Delivered]: 'success', [OrderStatus.Cancelled]: 'danger', [OrderStatus.DeliveryFailed]: 'danger', [OrderStatus.Returned]: 'neutral',
};

export function OrderCard({ order, available, busy, highlighted, onClaim, onTransition, onFailure }: {
  order: Order; available?: boolean; busy?: boolean; highlighted?: boolean; onClaim?(): void;
  onTransition?(status: OrderStatus): void | Promise<void>; onFailure?(reason: string): void;
}) {
  const [showFailure, setShowFailure] = useState(false);
  const [failureReason, setFailureReason] = useState('');
  const nextActions = order.allowedNextStatuses.filter(status => actionLabels[status]);
  const canFail = order.allowedNextStatuses.includes(OrderStatus.DeliveryFailed);
  const call = () => Linking.openURL(`tel:${order.customerPhone}`).catch(() => Alert.alert('Telefon açılamadı'));
  const destination = order.deliveryLatitude !== null && order.deliveryLongitude !== null
    ? `${order.deliveryLatitude},${order.deliveryLongitude}`
    : order.deliveryAddress;
  const navigate = () => Linking.openURL(`https://www.google.com/maps/dir/?api=1&destination=${encodeURIComponent(destination)}&travelmode=driving&dir_action=navigate`)
    .catch(() => Alert.alert('Harita açılamadı', 'Cihazda yol tarifi açabilecek bir harita uygulaması bulunamadı.'));
  const payment = describePayment(order);
  const confirmCollection = (status: OrderStatus) => new Promise<boolean>(resolve => {
    if (status !== OrderStatus.Delivered || !payment.collectAtDoor) return resolve(true);
    Alert.alert('Tahsilatı onayla', `${payment.amountText} ${payment.methodText} tahsil ettin mi?`, [
      { text: 'Hayır', style: 'cancel', onPress: () => resolve(false) },
      { text: 'Evet, tahsil ettim', onPress: () => resolve(true) },
    ], { cancelable: true, onDismiss: () => resolve(false) });
  });
  const transition = async (status: OrderStatus) => {
    if (!(await confirmCollection(status))) return;
    try {
      await onTransition?.(status);
      if (status === OrderStatus.OnTheWay) await navigate();
    } catch {
      // Mutation katmanı kullanıcıya API hatasını gösterir; durum değişmeden navigasyon açılmaz.
    }
  };
  const time = new Date(order.createdAtUtc).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' });

  return <Card style={highlighted ? styles.highlighted : undefined}>
    <View style={styles.topRow}>
      <Pill label={available ? 'Havuzda' : orderStatusLabels[order.status]} tone={available ? 'warning' : statusTones[order.status]} />
      <Text style={styles.meta}>{orderSourceLabels[order.source]} · {time}</Text>
    </View>

    <View style={styles.headline}>
      <View style={styles.flex}>
        <Text style={styles.customer} numberOfLines={1}>{order.customerName}</Text>
        <Text style={styles.orderNo}>{order.externalId ? `#${order.externalId}` : `#${order.id.slice(0, 8)}`}</Text>
      </View>
      <Text style={styles.amount}>{order.totalAmount.toLocaleString('tr-TR', { style: 'currency', currency: order.currency })}</Text>
    </View>

    <View style={styles.address}>
      <Ionicons name="location" size={18} color={colors.primary} />
      <Text style={styles.addressText}>{order.deliveryAddress}</Text>
    </View>
    {!available ? <View style={[styles.payment, payment.collectAtDoor ? styles.paymentCollect : payment.paid ? styles.paymentPaid : styles.paymentUnknown]}>
      <Ionicons name={payment.icon} size={18} color={payment.collectAtDoor ? colors.warning : payment.paid ? colors.success : colors.textMuted} />
      <Text style={[styles.paymentText, payment.collectAtDoor && styles.paymentTextStrong]}>{payment.label}</Text>
    </View> : null}
    {order.deliveryInstructions ? <View style={styles.instructions}><Ionicons name="information-circle-outline" size={17} color={colors.accent} /><Text style={styles.instructionsText}>{order.deliveryInstructions}</Text></View> : null}

    <View style={styles.quickActions}>
      <Pressable accessibilityRole="button" accessibilityLabel="Yol tarifi" onPress={navigate} style={({ pressed }) => [styles.quick, pressed && styles.quickPressed]}>
        <Ionicons name="navigate" size={17} color={colors.text} /><Text style={styles.quickText}>Yol tarifi</Text>
      </Pressable>
      <Pressable accessibilityRole="button" accessibilityLabel={`Ara ${order.customerPhone}`} onPress={call} style={({ pressed }) => [styles.quick, pressed && styles.quickPressed]}>
        <Ionicons name="call" size={16} color={colors.text} /><Text style={styles.quickText}>Ara</Text>
      </Pressable>
    </View>

    {available ? <Button size="lg" title="Paketi üstlen" onPress={() => onClaim?.()} loading={busy} icon={<Ionicons name="hand-left" size={17} color={colors.white} />} /> : null}
    {!available && nextActions.map(status => <Button key={status} size="lg" title={actionLabels[status]!} onPress={() => void transition(status)} loading={busy} icon={<Ionicons name={actionIcons[status]!} size={18} color={colors.white} />} />)}
    {!available && canFail && !showFailure ? <Button title="Teslim edilemedi" variant="ghost" onPress={() => setShowFailure(true)} icon={<Ionicons name="alert-circle-outline" size={17} color={colors.textSecondary} />} /> : null}
    {showFailure ? <View style={styles.failureBox}>
      <Text style={styles.failureTitle}>Teslim edilememe nedeni</Text>
      <TextInput value={failureReason} onChangeText={setFailureReason} placeholder="Örn. müşteriye ulaşılamadı" placeholderTextColor={colors.textFaint} multiline style={styles.input} />
      <View style={styles.failureActions}>
        <View style={styles.flex}><Button title="Vazgeç" variant="secondary" onPress={() => setShowFailure(false)} /></View>
        <View style={styles.flex}><Button title="Bildir" variant="danger" disabled={failureReason.trim().length < 3} loading={busy} onPress={() => onFailure?.(failureReason.trim())} /></View>
      </View>
    </View> : null}
  </Card>;
}

type PaymentView = { label: string; icon: keyof typeof Ionicons.glyphMap; collectAtDoor: boolean; paid: boolean; amountText: string; methodText: string };

function describePayment(order: Order): PaymentView {
  const amountText = order.totalAmount.toLocaleString('tr-TR', { style: 'currency', currency: order.currency });
  const paid = order.paymentStatus === PaymentStatus.Paid;
  const method = order.paymentMethod ?? PaymentMethod.Unspecified;
  if (paid) return { label: method === PaymentMethod.Online ? 'Online ödendi · tahsilat yok' : 'Ödendi · tahsilat yok', icon: 'checkmark-circle', collectAtDoor: false, paid, amountText, methodText: '' };
  if (method === PaymentMethod.Cash) return { label: `Kapıda NAKİT tahsil et: ${amountText}`, icon: 'cash-outline', collectAtDoor: true, paid, amountText, methodText: 'nakit' };
  if (method === PaymentMethod.Card) return { label: `Kapıda KART ile tahsil et: ${amountText}`, icon: 'card-outline', collectAtDoor: true, paid, amountText, methodText: 'kartla' };
  if (method === PaymentMethod.Online) return { label: 'Online ödendi · tahsilat yok', icon: 'checkmark-circle', collectAtDoor: false, paid: true, amountText, methodText: '' };
  return { label: 'Ödeme bilgisi yok · işletmeye danış', icon: 'help-circle-outline', collectAtDoor: false, paid, amountText, methodText: '' };
}

const styles = StyleSheet.create({
  payment: { flexDirection: 'row', alignItems: 'center', gap: 8, paddingVertical: 10, paddingHorizontal: 12, borderRadius: 12 },
  paymentCollect: { backgroundColor: colors.warningSoft },
  paymentPaid: { backgroundColor: colors.successSoft },
  paymentUnknown: { backgroundColor: colors.surfaceMuted },
  paymentText: { flex: 1, color: colors.textSecondary, fontSize: 14, fontWeight: '600' },
  paymentTextStrong: { color: colors.text, fontSize: 15, fontWeight: '700' },
  highlighted: { borderColor: colors.primary, borderWidth: 2 },
  topRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 12 },
  meta: { color: colors.textMuted, fontSize: 12, fontWeight: '500' },
  headline: { flexDirection: 'row', alignItems: 'flex-start', gap: 12 },
  customer: { color: colors.text, fontSize: 19, fontWeight: '700', letterSpacing: -0.3 },
  orderNo: { color: colors.textMuted, fontSize: 12, marginTop: 2, fontVariant: ['tabular-nums'] },
  amount: { color: colors.text, fontSize: 18, fontWeight: '700', fontVariant: ['tabular-nums'] },
  address: { flexDirection: 'row', alignItems: 'flex-start', gap: 8, padding: 12, borderRadius: 14, backgroundColor: colors.surfaceElevated, borderWidth: StyleSheet.hairlineWidth, borderColor: colors.border },
  addressText: { flex: 1, color: colors.textSecondary, fontSize: 15, lineHeight: 21 },
  instructions: { flexDirection: 'row', alignItems: 'flex-start', gap: 7, paddingHorizontal: 4 },
  instructionsText: { flex: 1, color: colors.textSecondary, fontSize: 13, lineHeight: 19 },
  quickActions: { flexDirection: 'row', gap: 8 },
  quick: { flex: 1, minHeight: 44, flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: 7, borderRadius: 12, backgroundColor: colors.surfaceMuted },
  quickPressed: { opacity: 0.75 },
  quickText: { color: colors.text, fontSize: 14, fontWeight: '600' },
  failureBox: { gap: 10, padding: 12, borderRadius: 14, backgroundColor: colors.dangerSoft },
  failureTitle: { color: colors.danger, fontSize: 13, fontWeight: '600' },
  input: { minHeight: 80, borderWidth: 1, borderColor: colors.borderStrong, borderRadius: 12, padding: 12, color: colors.text, fontSize: 15, textAlignVertical: 'top', backgroundColor: colors.surface },
  failureActions: { flexDirection: 'row', gap: 8 }, flex: { flex: 1 },
});
