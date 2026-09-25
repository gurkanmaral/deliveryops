import { useOperationsRealtime } from '@/shared/use-operations-realtime';
import { usePushNotifications } from '@/features/notifications/push-notifications';
import { colors } from '@/theme/colors';
import { Ionicons } from '@expo/vector-icons';
import { Tabs } from 'expo-router';
import { StyleSheet } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';

export default function AppLayout() {
  useOperationsRealtime();
  usePushNotifications();
  const insets = useSafeAreaInsets();
  return <Tabs screenOptions={{
    headerShown: false, tabBarActiveTintColor: colors.primary, tabBarInactiveTintColor: colors.textMuted,
    tabBarStyle: { backgroundColor: colors.surface, borderTopColor: colors.border, borderTopWidth: StyleSheet.hairlineWidth, height: 66 + insets.bottom, paddingTop: 6, paddingBottom: insets.bottom + 8, elevation: 0, shadowOpacity: 0 },
    tabBarLabelStyle: { fontSize: 11, fontWeight: '600' },
  }}>
    <Tabs.Screen name="index" options={{ title: 'Vardiya', tabBarIcon: ({ color, size, focused }) => <Ionicons name={focused ? 'pulse' : 'pulse-outline'} color={color} size={size - 1} /> }} />
    <Tabs.Screen name="orders" options={{ title: 'Paketlerim', tabBarIcon: ({ color, size, focused }) => <Ionicons name={focused ? 'bag-handle' : 'bag-handle-outline'} color={color} size={size - 1} /> }} />
    <Tabs.Screen name="available" options={{ title: 'Paket Havuzu', tabBarIcon: ({ color, size, focused }) => <Ionicons name={focused ? 'layers' : 'layers-outline'} color={color} size={size - 1} /> }} />
    <Tabs.Screen name="profile" options={{ title: 'Profil', tabBarIcon: ({ color, size, focused }) => <Ionicons name={focused ? 'person-circle' : 'person-circle-outline'} color={color} size={size - 1} /> }} />
  </Tabs>;
}
