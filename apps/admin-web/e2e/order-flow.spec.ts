import { expect, test, type APIRequestContext } from '@playwright/test'
import { accounts, apiLogin, bearer, coreApiUrl, loginThroughUi } from './helpers'

interface CurrentUser { businessId: string }
interface Branch { id: string }
interface Courier { id: string; branchId?: string; firstName: string; lastName: string; isActive: boolean }
interface PagedResponse<T> { items: T[] }
interface DispatchSettings {
  businessId: string
  autoConfirmOrders: boolean
  autoAssignCouriers: boolean
  allowCourierSelfClaim: boolean
  preferBranchCouriers: boolean
  maxActiveOrdersPerCourier: number
  requireFreshLocation: boolean
  locationFreshnessMinutes: number
  assignmentRadiusKm?: number
}

async function getJson<T>(request: APIRequestContext, path: string, token: string): Promise<T> {
  const response = await request.get(`${coreApiUrl}${path}`, { headers: bearer(token) })
  expect(response.ok()).toBeTruthy()
  return response.json() as Promise<T>
}

test('telefon siparişi oluşturulur, onaylanır ve aktif kuryeye atanır', async ({ page, request }) => {
  const token = await apiLogin(request, accounts.business)
  const platformToken = await apiLogin(request, accounts.platform)
  const userResponse = await request.get(`${process.env.AUTH_API_URL ?? 'http://localhost:5200'}/api/v1/auth/me`, { headers: bearer(token) })
  expect(userResponse.ok()).toBeTruthy()
  const user = await userResponse.json() as CurrentUser
  const branches = await getJson<PagedResponse<Branch>>(request, '/api/v1/branches?pageSize=100', token)
  const couriers = await getJson<PagedResponse<Courier>>(request, '/api/v1/couriers?pageSize=100', token)
  const courier = couriers.items.find(item => item.isActive)
  expect(courier).toBeTruthy()
  const branch = branches.items.find(item => item.id === courier?.branchId) ?? branches.items[0]
  expect(branch).toBeTruthy()

  const originalSettings = await getJson<DispatchSettings>(request, '/api/v1/dispatch/settings', token)
  const activeShifts = await getJson<{ items: Array<{ courierId: string }> }>(request, '/api/v1/shifts/active?pageSize=100', token)
  let startedShift = false
  const creditAccount = await getJson<{ balance: number }>(request, '/api/v1/credits/account', token)
  let creditAdjustment = 0
  let settingsChanged = false
  let orderId = ''
  try {
    const settingsResponse = await request.put(`${coreApiUrl}/api/v1/dispatch/settings`, {
      headers: bearer(token),
      data: { ...originalSettings, businessId: user.businessId, autoConfirmOrders: false, autoAssignCouriers: false },
    })
    expect(settingsResponse.ok()).toBeTruthy()
    settingsChanged = true

    if (!activeShifts.items.some(shift => shift.courierId === courier!.id)) {
      const startResponse = await request.post(`${coreApiUrl}/api/v1/shifts/couriers/${courier!.id}/start`, { headers: bearer(token) })
      expect(startResponse.ok()).toBeTruthy()
      startedShift = true
    }

    if (creditAccount.balance < 1) {
      creditAdjustment = 5
      const adjustResponse = await request.post(`${coreApiUrl}/api/v1/credits/adjustments`, {
        headers: bearer(platformToken, { 'Idempotency-Key': crypto.randomUUID() }),
        data: { businessId: user.businessId, amount: creditAdjustment, description: 'E2E geçici test kredisi' },
      })
      expect(adjustResponse.ok()).toBeTruthy()
    }

    await loginThroughUi(page, accounts.business)
    await page.getByRole('navigation', { name: 'Ana menü' }).getByRole('link', { name: 'Siparişler', exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Operasyon panosu' })).toBeVisible()

    const customerName = `E2E Müşteri ${Date.now()}`
    await page.locator('.order-form select').selectOption(branch.id)
    await page.getByLabel('Müşteri', { exact: true }).fill(customerName)
    await page.getByLabel('Telefon', { exact: true }).fill('5551112233')
    await page.getByLabel('Teslimat adresi', { exact: true }).fill('E2E Test Mahallesi No: 1')
    await page.getByLabel('Tutar', { exact: true }).fill('149.90')

    const createResponsePromise = page.waitForResponse(response => response.url().endsWith('/api/v1/orders/phone') && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Sipariş oluştur' }).click()
    const createResponse = await createResponsePromise
    expect(createResponse.status()).toBe(201)
    orderId = (await createResponse.json() as { id: string }).id

    const orderRow = page.getByRole('row').filter({ hasText: customerName })
    await expect(orderRow).toBeVisible()
    await expect(orderRow.getByText('Yeni', { exact: true })).toBeVisible()

    const confirmResponsePromise = page.waitForResponse(response => response.url().endsWith(`/api/v1/orders/${orderId}/status`) && response.request().method() === 'PATCH')
    await orderRow.getByRole('button', { name: 'Onayla' }).click()
    expect((await confirmResponsePromise).ok()).toBeTruthy()
    await expect(orderRow.getByText('Onaylandı', { exact: true })).toBeVisible()

    const assignResponsePromise = page.waitForResponse(response => response.url().endsWith(`/api/v1/orders/${orderId}/courier`) && response.request().method() === 'PUT')
    await orderRow.getByRole('combobox').selectOption(courier!.id)
    expect((await assignResponsePromise).ok()).toBeTruthy()
    await expect(orderRow.getByText('Atandı', { exact: true })).toBeVisible()
    await expect(orderRow.getByRole('combobox')).toHaveValue(courier!.id)
  } finally {
    if (orderId) {
      await request.delete(`${coreApiUrl}/api/v1/orders/${orderId}`, { headers: bearer(token) })
    }
    if (creditAdjustment) {
      await request.post(`${coreApiUrl}/api/v1/credits/adjustments`, {
        headers: bearer(platformToken, { 'Idempotency-Key': crypto.randomUUID() }),
        data: { businessId: user.businessId, amount: -creditAdjustment, description: 'E2E geçici test kredisi temizliği' },
      })
    }
    if (startedShift) {
      await request.post(`${coreApiUrl}/api/v1/shifts/couriers/${courier!.id}/end`, { headers: bearer(token) })
    }
    if (settingsChanged) {
      await request.put(`${coreApiUrl}/api/v1/dispatch/settings`, {
        headers: bearer(token),
        data: { ...originalSettings, businessId: user.businessId },
      })
    }
  }
})
