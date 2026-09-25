import { expect, test } from '@playwright/test'
import { accounts, loginThroughUi } from './helpers'

test('mutabakat kesinleştirilir ve PDF/Excel belgeleri indirilir', async ({ page }) => {
  await loginThroughUi(page, accounts.platform)
  const initialSettlementsResponse = page.waitForResponse(response => response.url().includes('/api/v1/billing/settlements?') && response.request().method() === 'GET')
  await page.getByRole('navigation', { name: 'Ana menü' }).getByRole('link', { name: 'Finans', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Mutabakat ve fiyatlandırma' })).toBeVisible()
  await initialSettlementsResponse
  const businessSelect = page.locator('.billing-toolbar select')
  const demoBusinessId = await businessSelect.locator('option', { hasText: 'Demo Restoran' }).getAttribute('value')
  if (demoBusinessId && await businessSelect.inputValue() !== demoBusinessId) {
    const selectedBusinessSettlementsResponse = page.waitForResponse(response => response.url().includes(`/api/v1/billing/settlements?businessId=${demoBusinessId}`) && response.request().method() === 'GET')
    await businessSelect.selectOption(demoBusinessId)
    await selectedBusinessSettlementsResponse
  }

  const history = page.getByRole('article', { name: 'Mutabakat geçmişi' })
  await expect(history).toBeVisible()
  if (await history.getByText('Kesinleşti', { exact: true }).count() === 0) {
    if (await history.getByText('Taslak', { exact: true }).count() === 0) {
      const createResponsePromise = page.waitForResponse(response => response.url().endsWith('/api/v1/billing/settlements') && response.request().method() === 'POST')
      await page.getByRole('button', { name: 'Taslağı kaydet' }).click()
      expect((await createResponsePromise).ok()).toBeTruthy()
      await expect(history.getByText('Taslak', { exact: true }).first()).toBeVisible()
    }
    const draftRow = history.getByRole('row').filter({ hasText: 'Taslak' }).first()
    const finalizeResponsePromise = page.waitForResponse(response => response.url().includes('/api/v1/billing/settlements/') && response.url().endsWith('/finalize') && response.request().method() === 'POST')
    await draftRow.getByRole('button', { name: 'Kesinleştir' }).click()
    expect((await finalizeResponsePromise).ok()).toBeTruthy()
  }

  await Promise.all([
    page.waitForURL(/\/login$/),
    page.getByRole('button', { name: 'Çıkış yap' }).click(),
  ])
  await loginThroughUi(page, accounts.business)
  const businessSettlementsResponse = page.waitForResponse(response =>
    response.url().includes('/api/v1/billing/settlements?') && response.request().method() === 'GET')
  await page.getByRole('navigation', { name: 'Ana menü' }).getByRole('link', { name: 'Mutabakat', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Mutabakat ve fiyatlandırma' })).toBeVisible()
  expect((await businessSettlementsResponse).ok()).toBeTruthy()
  const businessHistory = page.getByRole('article', { name: 'Mutabakat geçmişi' })

  const finalizedRow = businessHistory.getByRole('row').filter({ hasText: 'Kesinleşti' }).first()
  await expect(finalizedRow).toBeVisible()

  const pdfDownloadPromise = page.waitForEvent('download')
  await finalizedRow.getByRole('button', { name: 'PDF' }).click()
  const pdfDownload = await pdfDownloadPromise
  expect(pdfDownload.suggestedFilename()).toMatch(/\.pdf$/)
  expect(await pdfDownload.path()).toBeTruthy()

  const excelDownloadPromise = page.waitForEvent('download')
  await finalizedRow.getByRole('button', { name: 'Excel' }).click()
  const excelDownload = await excelDownloadPromise
  expect(excelDownload.suggestedFilename()).toMatch(/\.xlsx$/)
  expect(await excelDownload.path()).toBeTruthy()
})
