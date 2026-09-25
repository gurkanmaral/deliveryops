const stripTrailingSlash = (value: string) => value.replace(/\/$/, '');

export const env = {
  authApiUrl: stripTrailingSlash(process.env.EXPO_PUBLIC_AUTH_API_URL ?? 'http://localhost:5200'),
  coreApiUrl: stripTrailingSlash(process.env.EXPO_PUBLIC_CORE_API_URL ?? 'http://localhost:5100'),
  notificationsApiUrl: stripTrailingSlash(process.env.EXPO_PUBLIC_NOTIFICATIONS_API_URL ?? 'http://localhost:5300'),
  easProjectId: process.env.EXPO_PUBLIC_EAS_PROJECT_ID,
};
