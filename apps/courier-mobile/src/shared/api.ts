import { env } from '@/config/env';
import { clearSessionStorage, getAccessToken, getRefreshToken, saveTokens } from '@/features/auth/session-storage';
import type { MobileAuthResponse } from './types';

type ProblemDetails = { title?: string; detail?: string; errors?: Record<string, string[]> };

export class ApiError extends Error {
  constructor(public readonly status: number, message: string) {
    super(message);
    this.name = 'ApiError';
  }
}

async function parseResponse<T>(response: Response): Promise<T> {
  if (response.ok) return response.status === 204 ? (undefined as T) : response.json();
  let message = `İstek başarısız (${response.status}).`;
  try {
    const problem = (await response.json()) as ProblemDetails;
    message = problem.detail ?? problem.title ?? Object.values(problem.errors ?? {}).flat()[0] ?? message;
  } catch {
    // Some infrastructure errors intentionally have no JSON body.
  }
  throw new ApiError(response.status, message);
}

async function request<T>(baseUrl: string, path: string, init: RequestInit = {}, token?: string | null) {
  const headers = new Headers(init.headers);
  headers.set('Accept', 'application/json');
  if (init.body) headers.set('Content-Type', 'application/json');
  if (token) headers.set('Authorization', `Bearer ${token}`);
  return parseResponse<T>(await fetch(`${baseUrl}${path}`, { ...init, headers }));
}

let refreshPromise: Promise<string> | null = null;
let authenticationLostListener: (() => void) | null = null;

export function onAuthenticationLost(listener: () => void) {
  authenticationLostListener = listener;
  return () => {
    if (authenticationLostListener === listener) authenticationLostListener = null;
  };
}

async function refreshAccessToken() {
  if (!refreshPromise) {
    refreshPromise = (async () => {
      const refreshToken = await getRefreshToken();
      if (!refreshToken) throw new ApiError(401, 'Oturum süresi doldu.');
      const tokens = await request<MobileAuthResponse>(env.authApiUrl, '/api/v1/auth/mobile/refresh', {
        method: 'POST',
        body: JSON.stringify({ refreshToken }),
      });
      await saveTokens(tokens.accessToken, tokens.refreshToken);
      return tokens.accessToken;
    })().finally(() => { refreshPromise = null; });
  }
  return refreshPromise;
}

async function authenticatedApi<T>(baseUrl: string, path: string, init: RequestInit = {}) {
  const token = await getAccessToken();
  try {
    return await request<T>(baseUrl, path, init, token);
  } catch (error) {
    if (!(error instanceof ApiError) || error.status !== 401) throw error;
    try {
      const refreshedToken = await refreshAccessToken();
      return await request<T>(baseUrl, path, init, refreshedToken);
    } catch (refreshError) {
      await clearSessionStorage();
      authenticationLostListener?.();
      throw refreshError;
    }
  }
}

export const coreApi = <T>(path: string, init: RequestInit = {}) => authenticatedApi<T>(env.coreApiUrl, path, init);
export const notificationsApi = <T>(path: string, init: RequestInit = {}) => authenticatedApi<T>(env.notificationsApiUrl, path, init);
export const authApi = <T>(path: string, init: RequestInit = {}) => request<T>(env.authApiUrl, path, init);
