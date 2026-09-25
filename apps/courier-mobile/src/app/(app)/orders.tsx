import { EmptyState, PageHeader, Pill, Screen } from '@/components/ui';
import { useAuth } from '@/features/auth/auth-context';
import { OrderCard } from '@/features/orders/order-card';
import { changeOrderStatus, getOrders, orderKeys, reportDeliveryFailure } from '@/features/orders/order-api';
import { OrderStatus } from '@/shared/types';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useLocalSearchParams } from 'expo-router';
import { colors } from '@/theme/colors';
import { Ionicons } from '@expo/vector-icons';
import { Alert, RefreshControl, ScrollView } from 'react-native';

const terminalStatuses = [OrderStatus.Delivered, OrderStatus.Cancelled, OrderStatus.Returned];

export default function OrdersScreen() {
  const { orderId } = useLocalSearchParams<{ orderId?: string }>();
  const { profile, refreshProfile } = useAuth();
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: orderKeys.all, queryFn: getOrders });
  const mutation = useMutation({
    mutationFn: ({ id, status, reason }: { id: string; status: OrderStatus; reason?: string }) => status === OrderStatus.DeliveryFailed ? reportDeliveryFailure(id, reason!) : changeOrderStatus(id, status),
    onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: orderKeys.all }); await refreshProfile(); },
    onError: (error: Error) => Alert.alert('Paket güncellenemedi', error.message),
  });
  const items = (query.data?.items.filter(x => x.courierId === profile?.id && !terminalStatuses.includes(x.status)) ?? [])
    .sort((left, right) => Number(right.id === orderId) - Number(left.id === orderId));
  return <Screen scroll={false}><PageHeader eyebrow="Teslimat akışı" title="Paketlerim" description="Atanan paketlerini sırayla ilerlet." right={items.length ? <Pill label={`${items.length} aktif`} tone="primary" /> : undefined} />
    <ScrollView contentContainerStyle={{ gap: 12, paddingBottom: 120 }} showsVerticalScrollIndicator={false} refreshControl={<RefreshControl tintColor={colors.primary} colors={[colors.primary]} refreshing={query.isRefetching} onRefresh={query.refetch} />}>
      {items.length ? items.map(order => <OrderCard key={order.id} order={order} highlighted={order.id === orderId} busy={mutation.isPending && mutation.variables?.id === order.id} onTransition={status => mutation.mutateAsync({ id: order.id, status }).then(() => undefined)} onFailure={reason => mutation.mutate({ id: order.id, status: OrderStatus.DeliveryFailed, reason })} />) : <EmptyState icon={<Ionicons name="bag-handle-outline" size={28} color={colors.textMuted} />} title="Aktif paketin yok" description="Atanan veya üstlendiğin paketler burada görünecek." />}
    </ScrollView>
  </Screen>;
}
