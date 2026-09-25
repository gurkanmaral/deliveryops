#!/usr/bin/env node

import { execFileSync, spawnSync } from 'node:child_process'
import { resolve } from 'node:path'

const root = resolve(import.meta.dirname, '..')
const coreUrl = process.env.CORE_API_URL ?? 'http://localhost:5100'
const authUrl = process.env.AUTH_API_URL ?? 'http://localhost:5200'
const metroUrl = process.env.METRO_URL ?? 'http://localhost:8081'
const courierEmail = process.env.COURIER_EMAIL ?? 'courier@demo.local'
const courierPassword = process.env.COURIER_PASSWORD ?? 'CourierDemo123!'
const json = value => JSON.stringify(value)

async function request(url, { token, headers, expected = [200], ...options } = {}) {
  const response = await fetch(url, {
    ...options,
    headers: {
      ...(options.body ? { 'Content-Type': 'application/json' } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...headers,
    },
  })
  const text = await response.text()
  if (!expected.includes(response.status)) {
    throw new Error(`${options.method ?? 'GET'} ${url} -> ${response.status}: ${text.slice(0, 800)}`)
  }
  if (!text) return undefined
  const contentType = response.headers.get('content-type') ?? ''
  return contentType.includes('json') ? JSON.parse(text) : text
}

async function login(email, password, mobile = false) {
  return request(`${authUrl}/api/v1/auth/${mobile ? 'mobile/login' : 'login'}`, {
    method: 'POST', body: json({ email, password }),
  })
}

function bootedSimulator() {
  const devices = JSON.parse(execFileSync('xcrun', ['simctl', 'list', 'devices', 'booted', '--json'], { encoding: 'utf8' })).devices
  const device = Object.values(devices).flat().find(item => item.state === 'Booted')
  if (!device) throw new Error('Çalışan iOS Simulator bulunamadı.')
  execFileSync('xcrun', ['simctl', 'get_app_container', device.udid, 'com.deliveryops.courier', 'app'], { stdio: 'ignore' })
  return device.udid
}

async function main() {
  console.log('DeliveryOps kurye iOS Simulator E2E')
  for (const url of [coreUrl, authUrl, metroUrl]) await request(url === metroUrl ? url : `${url}/health`)
  const udid = bootedSimulator()
  const [businessAuth, platformAuth, courierAuth] = await Promise.all([
    login('business@demo.local', 'BusinessDemo123!'),
    login('admin@deliveryops.local', 'DeliveryOps123!'),
    login(courierEmail, courierPassword, true),
  ])
  const businessToken = businessAuth.accessToken
  const platformToken = platformAuth.accessToken
  const courierToken = courierAuth.accessToken
  const [me, courier, branches, originalSettings, account] = await Promise.all([
    request(`${authUrl}/api/v1/auth/me`, { token: businessToken }),
    request(`${coreUrl}/api/v1/couriers/me`, { token: courierToken }),
    request(`${coreUrl}/api/v1/branches?page=1&pageSize=100`, { token: businessToken }),
    request(`${coreUrl}/api/v1/dispatch/settings`, { token: businessToken }),
    request(`${coreUrl}/api/v1/credits/account`, { token: businessToken }),
  ])
  const branch = branches.items.find(item => item.id === courier.branchId) ?? branches.items[0]
  if (!me.businessId || !branch) throw new Error('Demo işletmesi, kurye şubesi veya aktif şube bulunamadı.')

  const customer = `Mobile E2E ${Date.now()}`
  let orderId
  let creditAdjustment = 0
  try {
    if (courier.isShiftActive) {
      await request(`${coreUrl}/api/v1/shifts/couriers/${courier.id}/end`, { token: businessToken, method: 'POST' })
    }
    await request(`${coreUrl}/api/v1/dispatch/settings`, {
      token: businessToken, method: 'PUT',
      body: json({ ...originalSettings, businessId: me.businessId, autoConfirmOrders: false, autoAssignCouriers: false, allowCourierSelfClaim: true, maxActiveOrdersPerCourier: Math.max(10, originalSettings.maxActiveOrdersPerCourier) }),
    })
    if (account.balance < 1) {
      creditAdjustment = 5
      await request(`${coreUrl}/api/v1/credits/adjustments`, {
        token: platformToken, method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() },
        body: json({ businessId: me.businessId, amount: creditAdjustment, description: 'Mobile E2E geçici test kredisi' }),
      })
    }
    const order = await request(`${coreUrl}/api/v1/orders/phone`, {
      token: businessToken, method: 'POST', expected: [201],
      headers: { 'Idempotency-Key': crypto.randomUUID() },
      body: json({ businessId: me.businessId, branchId: branch.id, customerName: customer, customerPhone: '5550002001', deliveryAddress: 'Mobile E2E Mahallesi No: 1', totalAmount: 159.90 }),
    })
    orderId = order.id
    await request(`${coreUrl}/api/v1/orders/${orderId}/status`, { token: businessToken, method: 'PATCH', body: json({ status: 1 }) })
    await request(`${coreUrl}/api/v1/orders/${orderId}/status`, { token: businessToken, method: 'PATCH', body: json({ status: 2 }) })

    const devClientUrl = `exp+deliveryops-courier://expo-development-client/?url=${encodeURIComponent(metroUrl)}`
    const result = spawnSync('maestro', [
      'test', '--udid', udid,
      '-e', `DEV_CLIENT_URL=${devClientUrl}`,
      '-e', `COURIER_EMAIL=${courierEmail}`,
      '-e', `COURIER_PASSWORD=${courierPassword}`,
      '-e', `TEST_CUSTOMER=${customer}`,
      '--test-output-dir', resolve(root, 'artifacts/mobile-e2e'),
      resolve(root, 'apps/courier-mobile/.maestro/courier-order-lifecycle.yaml'),
    ], { cwd: root, stdio: 'inherit', env: { ...process.env, MAESTRO_CLI_NO_ANALYTICS: '1', MAESTRO_CLI_ANALYSIS_NOTIFICATION_DISABLED: 'true' } })
    if (result.status !== 0) throw new Error(`Maestro ${result.status ?? 'bilinmeyen'} koduyla başarısız oldu.`)
    console.log(`✓ Giriş, vardiya, self-claim ve teslimat akışı tamamlandı (${orderId}).`)
  } finally {
    if (orderId) {
      const order = await request(`${coreUrl}/api/v1/orders/${orderId}`, { token: businessToken }).catch(() => undefined)
      if (order && order.status < 3) {
        await request(`${coreUrl}/api/v1/orders/${orderId}`, { token: businessToken, method: 'DELETE' }).catch(() => undefined)
      } else if (order && order.status < 6) {
        const transitions = order.status === 3 ? [4, 5, 6] : order.status === 4 ? [5, 6] : [6]
        for (const status of transitions) {
          await request(`${coreUrl}/api/v1/orders/${orderId}/status`, { token: courierToken, method: 'PATCH', body: json({ status }) }).catch(() => undefined)
        }
      }
      await request(`${coreUrl}/api/v1/credits/refunds`, {
        token: platformToken, method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() },
        body: json({ businessId: me.businessId, orderId, description: 'Mobile E2E siparişi kredi iadesi' }),
      }).catch(() => undefined)
    }
    const freshCourier = await request(`${coreUrl}/api/v1/couriers/me`, { token: courierToken }).catch(() => undefined)
    if (freshCourier?.isShiftActive) {
      await request(`${coreUrl}/api/v1/shifts/couriers/${courier.id}/end`, { token: businessToken, method: 'POST' }).catch(() => undefined)
    }
    await request(`${coreUrl}/api/v1/dispatch/settings`, {
      token: businessToken, method: 'PUT', body: json({ ...originalSettings, businessId: me.businessId }),
    }).catch(() => undefined)
    if (creditAdjustment) {
      await request(`${coreUrl}/api/v1/credits/adjustments`, {
        token: platformToken, method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() },
        body: json({ businessId: me.businessId, amount: -creditAdjustment, description: 'Mobile E2E geçici kredi temizliği' }),
      }).catch(() => undefined)
    }
  }
}

main().catch(error => { console.error(`✗ ${error.message}`); process.exitCode = 1 })
