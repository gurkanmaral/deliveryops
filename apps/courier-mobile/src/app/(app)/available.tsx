import { EmptyState, PageHeader, Pill, Screen } from '@/components/ui';
import { useAuth } from '@/features/auth/auth-context';
import { AvailableOrderCard } from '@/features/orders/available-order-card';
import { claimOrder, getAvailableOrders, orderKeys } from '@/features/orders/order-api';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useLocalSearchParams } from 'expo-router';
import { colors } from '@/theme/colors';
import { Ionicons } from '@expo/vector-icons';
import { Alert, RefreshControl, ScrollView } from 'react-native';

export default function AvailableScreen() {
  const { orderId } = useLocalSearchParams<{ orderId?: string }>();
  const { profile, refreshProfile } = useAuth();
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: orderKeys.available, queryFn: getAvailableOrders });
  const mutation = useMutation({ mutationFn: claimOrder, onSuccess: async () => { await queryClient.invalidateQueries({ queryKey: orderKeys.all }); await refreshProfile(); }, onError: (error: Error) => Alert.alert('Paket üstlenilemedi', error.message) });
  const errorMessage = query.error instanceof Error ? query.error.message : null;
  return <Screen scroll={false}><PageHeader eyebrow="Yakındaki paketler" title="Paket havuzu" description={profile?.isShiftActive ? 'Konumuna yakın şubelerdeki paketler mesafeye göre sıralanır.' : 'Yakındaki paketleri görmek için önce vardiyanı başlat.'} right={query.data?.totalCount ? <Pill label={`${query.data.totalCount} paket`} tone="warning" /> : undefined} />
    <ScrollView contentContainerStyle={{ gap: 12, paddingBottom: 120 }} showsVerticalScrollIndicator={false} refreshControl={<RefreshControl tintColor={colors.primary} colors={[colors.primary]} refreshing={query.isRefetching} onRefresh={query.refetch} />}>
      {query.isError ? <EmptyState icon={<Ionicons name="location-outline" size={28} color={colors.textMuted} />} title="Konum bilgisi gerekli" description={errorMessage ?? 'Yakındaki paketleri gösterebilmek için güncel konumunu paylaş.'} />
        : query.data?.items.length ? [...query.data.items].sort((left, right) => Number(right.id === orderId) - Number(left.id === orderId)).map(order => <AvailableOrderCard key={order.id} order={order} highlighted={order.id === orderId} busy={mutation.isPending && mutation.variables === order.id} onClaim={() => mutation.mutate(order.id)} />)
          : <EmptyState icon={<Ionicons name="layers-outline" size={28} color={colors.textMuted} />} title="Yakında bekleyen paket yok" description="Üstlenme yarıçapına yeni bir paket girdiğinde bu listeye düşecek." />}
    </ScrollView>
  </Screen>;
}
