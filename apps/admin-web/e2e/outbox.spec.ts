import { expect, test } from '@playwright/test'
import { accounts, loginThroughUi } from './helpers'

test('dead-letter mesajı panelden görülür ve yeniden denenir', async ({ page }) => {
  await loginThroughUi(page, accounts.platform)
  const now = new Date().toISOString()
  await page.route('**/api/v1/operations/integration-outbox?*', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({ items: [{
      id: '11111111-1111-1111-1111-111111111111',
      businessId: '22222222-2222-2222-2222-222222222222',
      orderId: '33333333-3333-3333-3333-333333333333',
      eventType: 'OrderStatusChanged', attempts: 8, lastHttpStatusCode: 409,
      lastError: 'Kaynak entegrasyon olayı henüz hazır değil.',
      createdAtUtc: now, nextAttemptAtUtc: now, deadLetteredAtUtc: now,
    }], page: 1, pageSize: 100, totalCount: 1, totalPages: 1, hasPreviousPage: false, hasNextPage: false }),
  }))
  await page.route('**/api/v1/operations/notification-outbox?*', route => route.fulfill({
    status: 200, contentType: 'application/json',
    body: JSON.stringify({ items: [], page: 1, pageSize: 100, totalCount: 0, totalPages: 0, hasPreviousPage: false, hasNextPage: false }),
  }))
  await page.route('**/api/v1/operations/integration-outbox/*/retry', route => route.fulfill({
    status: 202, contentType: 'application/json',
    body: JSON.stringify({ id: '11111111-1111-1111-1111-111111111111', nextAttemptAtUtc: now }),
  }))

  await page.getByRole('navigation', { name: 'Ana menü' }).getByRole('link', { name: 'Hata Kuyrukları' }).click()
  await expect(page.getByRole('heading', { name: 'Hata kuyrukları' })).toBeVisible()
  await expect(page.getByText('Kaynak entegrasyon olayı henüz hazır değil.')).toBeVisible()
  await expect(page.getByText('HTTP 409', { exact: true })).toBeVisible()

  const retryRequest = page.waitForRequest(request => request.url().endsWith('/retry') && request.method() === 'POST')
  await page.getByRole('button', { name: 'Tekrar dene' }).click()
  await retryRequest
})
