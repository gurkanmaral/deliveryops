import { clearLocationQueue, stopBackgroundLocation } from '@/features/location/location-task';
import { authApi, coreApi, onAuthenticationLost } from '@/shared/api';
import type { CourierProfile, MobileAuthResponse } from '@/shared/types';
import { QueryClient, useQueryClient } from '@tanstack/react-query';
import { createContext, PropsWithChildren, use, useCallback, useEffect, useMemo, useState } from 'react';
import { clearSessionStorage, getRefreshToken, saveCourierId, saveTokens } from './session-storage';

type AuthContextValue = {
  profile: CourierProfile | null;
  isLoading: boolean;
  signIn(email: string, password: string): Promise<void>;
  signOut(): Promise<void>;
  refreshProfile(): Promise<CourierProfile>;
};

const AuthContext = createContext<AuthContextValue | null>(null);

async function loadProfile() {
  const profile = await coreApi<CourierProfile>('/api/v1/couriers/me');
  await saveCourierId(profile.id);
  return profile;
}

async function clearClientSession(queryClient: QueryClient) {
  await stopBackgroundLocation().catch(() => undefined);
  await clearLocationQueue().catch(() => undefined);
  await clearSessionStorage();
  queryClient.clear();
}

export function AuthProvider({ children }: PropsWithChildren) {
  const queryClient = useQueryClient();
  const [profile, setProfile] = useState<CourierProfile | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const refreshProfile = useCallback(async () => {
    const nextProfile = await loadProfile();
    setProfile(nextProfile);
    queryClient.setQueryData(['courier-profile'], nextProfile);
    return nextProfile;
  }, [queryClient]);

  useEffect(() => {
    let active = true;
    (async () => {
      try {
        if (!(await getRefreshToken())) return;
        const nextProfile = await loadProfile();
        if (active) setProfile(nextProfile);
      } catch {
        await clearClientSession(queryClient);
      } finally {
        if (active) setIsLoading(false);
      }
    })();
    return () => { active = false; };
  }, [queryClient]);

  useEffect(() => onAuthenticationLost(() => {
    setProfile(null);
    queryClient.clear();
    void stopBackgroundLocation().catch(() => undefined);
    void clearLocationQueue().catch(() => undefined);
  }), [queryClient]);

  const value = useMemo<AuthContextValue>(() => ({
    profile,
    isLoading,
    signIn: async (email, password) => {
      queryClient.clear();
      const tokens = await authApi<MobileAuthResponse>('/api/v1/auth/mobile/login', {
        method: 'POST',
        body: JSON.stringify({ email: email.trim(), password }),
      });
      await saveTokens(tokens.accessToken, tokens.refreshToken);
      try {
        await refreshProfile();
      } catch (error) {
        await clearClientSession(queryClient);
        throw error;
      }
    },
    signOut: async () => {
      const refreshToken = await getRefreshToken();
      if (refreshToken) {
        await authApi<void>('/api/v1/auth/mobile/logout', {
          method: 'POST',
          body: JSON.stringify({ refreshToken }),
        }).catch(() => undefined);
      }
      setProfile(null);
      await clearClientSession(queryClient);
    },
    refreshProfile,
  }), [isLoading, profile, queryClient, refreshProfile]);

  return <AuthContext value={value}>{children}</AuthContext>;
}

export function useAuth() {
  const context = use(AuthContext);
  if (!context) throw new Error('useAuth must be used inside AuthProvider.');
  return context;
}
