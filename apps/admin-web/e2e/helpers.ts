import { expect, type APIRequestContext, type Page } from '@playwright/test'

export const authApiUrl = process.env.AUTH_API_URL ?? 'http://localhost:5200'
export const coreApiUrl = process.env.CORE_API_URL ?? 'http://localhost:5100'

export const accounts = {
  platform: { email: 'admin@deliveryops.local', password: 'DeliveryOps123!' },
  business: { email: 'business@demo.local', password: 'BusinessDemo123!' },
  staff: { email: 'staff@demo.local', password: 'StaffDemo123!' },
} as const

export async function loginThroughUi(page: Page, account: { email: string; password: string }) {
  await page.goto('/login')
  await page.getByLabel('E-posta', { exact: true }).fill(account.email)
  await page.getByLabel('Şifre', { exact: true }).fill(account.password)
  await page.getByRole('button', { name: 'Giriş yap', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Genel bakış' })).toBeVisible()
}

export async function apiLogin(request: APIRequestContext, account: { email: string; password: string }) {
  const response = await request.post(`${authApiUrl}/api/v1/auth/login`, { data: account })
  expect(response.ok()).toBeTruthy()
  return (await response.json() as { accessToken: string }).accessToken
}

export function bearer(token: string, extra: Record<string, string> = {}) {
  return { Authorization: `Bearer ${token}`, ...extra }
}
