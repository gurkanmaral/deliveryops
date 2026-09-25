import { LoadingScreen } from '@/components/ui';
import { AuthProvider, useAuth } from '@/features/auth/auth-context';
import '@/features/location/location-task';
import { colors } from '@/theme/colors';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Stack } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { useState } from 'react';
import { SafeAreaProvider } from 'react-native-safe-area-context';

function Navigation() {
  const { profile, isLoading } = useAuth();
  if (isLoading) return <LoadingScreen />;
  return <><StatusBar style="dark" /><Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: colors.background } }}>
    <Stack.Protected guard={!profile}><Stack.Screen name="sign-in" /></Stack.Protected>
    <Stack.Protected guard={!!profile}><Stack.Screen name="(app)" /></Stack.Protected>
  </Stack></>;
}

export default function RootLayout() {
  const [queryClient] = useState(() => new QueryClient({ defaultOptions: { queries: { staleTime: 15_000, retry: 1 } } }));
  return <SafeAreaProvider><QueryClientProvider client={queryClient}><AuthProvider><Navigation /></AuthProvider></QueryClientProvider></SafeAreaProvider>;
}
