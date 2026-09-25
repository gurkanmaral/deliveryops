import { Button, Card, Pill } from '@/components/ui';
import { orderSourceLabels } from '@/shared/labels';
import type { AvailableOrder } from '@/shared/types';
import { colors } from '@/theme/colors';
import { Ionicons } from '@expo/vector-icons';
import { StyleSheet, Text, View } from 'react-native';

export function AvailableOrderCard({ order, busy, highlighted, onClaim }: {
  order: AvailableOrder;
  busy?: boolean;
  highlighted?: boolean;
  onClaim(): void;
}) {
  const createdTime = new Date(order.createdAtUtc).toLocaleTimeString('tr-TR', {
    hour: '2-digit', minute: '2-digit',
  });
  const waitingLabel = order.waitingMinutes < 1 ? 'Yeni' : `${order.waitingMinutes} dk bekliyor`;

  return <Card style={highlighted ? styles.highlighted : undefined}>
    <View style={styles.topRow}>
      <Pill label={`${order.pickupDistanceKm.toLocaleString('tr-TR')} km yakında`} tone="success" />
      <Text style={styles.meta}>{orderSourceLabels[order.source]} · {createdTime}</Text>
    </View>

    <View style={styles.headline}>
      <View style={styles.flex}>
        <Text style={styles.pickupName} numberOfLines={1}>{order.pickupName}</Text>
        <Text style={styles.waiting}>{waitingLabel}</Text>
      </View>
      <Text style={styles.amount}>{order.totalAmount.toLocaleString('tr-TR', {
        style: 'currency', currency: order.currency,
      })}</Text>
    </View>

    <View style={styles.pickup}>
      <Ionicons name="storefront-outline" size={18} color={colors.primary} />
      <View style={styles.flex}>
        <Text style={styles.pickupLabel}>Paket alınacak şube</Text>
        <Text style={styles.pickupAddress}>{order.pickupAddress}</Text>
      </View>
    </View>

    <View style={styles.privacy}>
      <Ionicons name="shield-checkmark-outline" size={17} color={colors.textMuted} />
      <Text style={styles.privacyText}>Müşteri bilgileri ve teslimat adresi, paketi üstlendikten sonra gösterilir.</Text>
    </View>

    <Button size="lg" title="Paketi üstlen" onPress={onClaim} loading={busy}
      icon={<Ionicons name="hand-left" size={17} color={colors.white} />} />
  </Card>;
}

const styles = StyleSheet.create({
  highlighted: { borderColor: colors.primary, borderWidth: 2 },
  topRow: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', gap: 12 },
  meta: { color: colors.textMuted, fontSize: 12, fontWeight: '500' },
  headline: { flexDirection: 'row', alignItems: 'flex-start', gap: 12 },
  flex: { flex: 1 },
  pickupName: { color: colors.text, fontSize: 19, fontWeight: '700', letterSpacing: -0.3 },
  waiting: { color: colors.primaryDark, fontSize: 12, fontWeight: '600', marginTop: 3 },
  amount: { color: colors.text, fontSize: 18, fontWeight: '700', fontVariant: ['tabular-nums'] },
  pickup: { flexDirection: 'row', alignItems: 'flex-start', gap: 9, padding: 12, borderRadius: 14, backgroundColor: colors.surfaceElevated, borderWidth: StyleSheet.hairlineWidth, borderColor: colors.border },
  pickupLabel: { color: colors.textMuted, fontSize: 11, fontWeight: '600', textTransform: 'uppercase', letterSpacing: 0.5, marginBottom: 3 },
  pickupAddress: { color: colors.textSecondary, fontSize: 14, lineHeight: 20 },
  privacy: { flexDirection: 'row', alignItems: 'flex-start', gap: 8, paddingHorizontal: 3 },
  privacyText: { flex: 1, color: colors.textMuted, fontSize: 12, lineHeight: 18 },
});
