import { expect, test } from '@playwright/test'
import { accounts, loginThroughUi } from './helpers'

test.describe('Panel kimlik ve tenant görünümü', () => {
  test('platform yöneticisi global menüyü ve işletmeleri görür', async ({ page }) => {
    await loginThroughUi(page, accounts.platform)

    await expect(page.getByText('Sistem Yöneticisi', { exact: true })).toBeVisible()
    const navigation = page.getByRole('navigation', { name: 'Ana menü' })
    await expect(navigation.getByRole('link', { name: 'İşletmeler', exact: true })).toBeVisible()
    await navigation.getByRole('link', { name: 'İşletmeler', exact: true }).click()
    await expect(page.getByRole('heading', { name: 'İşletmeler ve şubeler' })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Yeni işletme' })).toBeVisible()
  })

  test('işletme yöneticisi yalnızca kendi işletme görünümünü alır', async ({ page }) => {
    await loginThroughUi(page, accounts.business)

    await expect(page.getByText('İşletme Yöneticisi', { exact: true })).toBeVisible()
    const navigation = page.getByRole('navigation', { name: 'Ana menü' })
    await expect(navigation.getByRole('link', { name: 'Şubelerim', exact: true })).toBeVisible()
    await expect(navigation.getByRole('link', { name: 'İşletmeler', exact: true })).toHaveCount(0)
    await navigation.getByRole('link', { name: 'Şubelerim', exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Demo Restoran', level: 1 })).toBeVisible()
    await expect(page.getByRole('heading', { name: 'Yeni işletme' })).toHaveCount(0)
  })

  test('işletme personeli yönetim yazma araçlarını görmez', async ({ page }) => {
    await loginThroughUi(page, accounts.staff)

    await expect(page.getByText('İşletme Personeli', { exact: true })).toBeVisible()
    await page.getByRole('navigation', { name: 'Ana menü' }).getByRole('link', { name: 'Şubelerim', exact: true }).click()
    await expect(page.getByRole('button', { name: 'Şube ekle' })).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Pasife al' })).toHaveCount(0)
  })
})
