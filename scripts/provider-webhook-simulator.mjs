#!/usr/bin/env node

const coreUrl = process.env.CORE_API_URL ?? 'http://localhost:5100'
const authUrl = process.env.AUTH_API_URL ?? 'http://localhost:5200'
const integrationsUrl = process.env.INTEGRATIONS_API_URL ?? 'http://localhost:5400'
const providerName = (process.argv[2] ?? 'all').toLowerCase()
const providers = providerName === 'all' ? ['yemeksepeti', 'getir'] : [providerName]

if (providers.some(provider => !['yemeksepeti', 'getir'].includes(provider))) {
  console.error('Kullanım: node scripts/provider-webhook-simulator.mjs [all|yemeksepeti|getir]')
  process.exit(2)
}

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

const json = value => JSON.stringify(value)
const sleep = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds))

async function login(email, password) {
  return (await request(`${authUrl}/api/v1/auth/login`, {
    method: 'POST', body: json({ email, password }),
  })).accessToken
}

async function waitForCompletedEvent(token, connectionId, externalEventId) {
  for (let attempt = 0; attempt < 40; attempt += 1) {
    const events = await request(`${integrationsUrl}/api/v1/integrations/events?connectionId=${connectionId}&take=20`, { token })
    const event = events.find(item => item.externalEventId === externalEventId)
    if (event?.status === 2) return event
    if (event?.status === 4) throw new Error(`Webhook dead-letter oldu: ${event.lastError}`)
    await sleep(500)
  }
  throw new Error(`${externalEventId} olayı 20 saniye içinde tamamlanmadı.`)
}

async function runProvider(provider, businessToken, platformToken, businessId, branchId) {
  const suffix = `${Date.now()}-${crypto.randomUUID().slice(0, 8)}`
  const isYemeksepeti = provider === 'yemeksepeti'
  const externalOrderId = `${isYemeksepeti ? 'YS' : 'GETIR'}-LOCAL-${suffix}`
  const externalEventId = `getir-created-${suffix}`
  let connectionId
  let coreOrderId
  try {
    const connection = await request(`${integrationsUrl}/api/v1/integrations`, {
      token: businessToken, method: 'POST', expected: [201],
      body: json({
        businessId, branchId, provider: isYemeksepeti ? 0 : 1,
        name: `Local ${isYemeksepeti ? 'Yemeksepeti' : 'Getir'} Simulator ${suffix}`,
        authMode: 0,
        adapterVersion: isYemeksepeti ? 'yemeksepeti-partner-v2' : 'canonical-v1',
      }),
    })
    connectionId = connection.id
    const payload = isYemeksepeti ? {
      order_id: externalOrderId,
      status: 'RECEIVED',
      order_type: 'DELIVERY',
      customer: {
        first_name: 'Local', last_name: 'Yemeksepeti', phone_number: '5550001001',
        delivery_address: { formattedAddress: 'Webhook Test Mahallesi No: 1' },
      },
      payment: { order_total: 189.90 },
      sys: { updated_at: new Date().toISOString() },
      items: [{ id: 'test-item', name: 'Test menü', quantity: 1 }],
    } : {
      eventId: externalEventId,
      eventType: 'order.created',
      externalOrderId,
      customerName: 'Local Getir', customerPhone: '5550001002',
      deliveryAddress: 'Webhook Test Caddesi No: 2', totalAmount: 209.90,
    }
    const accepted = await request(`${integrationsUrl}${connection.webhookPath}`, {
      method: 'POST', expected: [202], headers: { 'X-DeliveryOps-Key': connection.secret }, body: json(payload),
    })
    const event = await waitForCompletedEvent(businessToken, connectionId,
      isYemeksepeti ? `${externalOrderId}:RECEIVED:${payload.sys.updated_at}` : externalEventId)
    coreOrderId = event.coreOrderId
    const order = await request(`${coreUrl}/api/v1/orders/${coreOrderId}`, { token: businessToken })
    if (order.externalId !== externalOrderId || order.source !== (isYemeksepeti ? 3 : 4)) {
      throw new Error('Core siparişi sağlayıcı kimliği/kaynağı ile eşleşmedi.')
    }

    const duplicate = await request(`${integrationsUrl}${connection.webhookPath}`, {
      method: 'POST', expected: [200], headers: { 'X-DeliveryOps-Key': connection.secret }, body: json(payload),
    })
    if (!duplicate.duplicate || duplicate.eventId !== accepted.eventId) {
      throw new Error('Webhook idempotency kontrolü başarısız.')
    }
    console.log(`  ✓ ${isYemeksepeti ? 'Yemeksepeti' : 'Getir'} webhook -> Core siparişi ${coreOrderId}`)
    console.log('  ✓ Aynı webhook tekrarında idempotent cevap')
  } finally {
    if (coreOrderId) {
      await request(`${coreUrl}/api/v1/orders/${coreOrderId}`, { token: businessToken, method: 'DELETE' }).catch(() => undefined)
      await request(`${coreUrl}/api/v1/credits/refunds`, {
        token: platformToken, method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() },
        body: json({ businessId, orderId: coreOrderId, description: `${provider} webhook simülatörü kredi iadesi` }),
      }).catch(() => undefined)
    }
    if (connectionId) {
      await request(`${integrationsUrl}/api/v1/integrations/${connectionId}/active`, {
        token: businessToken, method: 'PATCH', body: json({ isActive: false }),
      }).catch(() => undefined)
    }
  }
}

async function main() {
  console.log('DeliveryOps sağlayıcı webhook simülatörü')
  for (const url of [coreUrl, authUrl, integrationsUrl]) await request(`${url}/health`)
  const [businessToken, platformToken] = await Promise.all([
    login('business@demo.local', 'BusinessDemo123!'),
    login('admin@deliveryops.local', 'DeliveryOps123!'),
  ])
  const me = await request(`${authUrl}/api/v1/auth/me`, { token: businessToken })
  const branches = await request(`${coreUrl}/api/v1/branches?page=1&pageSize=100`, { token: businessToken })
  const branch = branches.items.find(item => item.isActive) ?? branches.items[0]
  if (!me.businessId || !branch) throw new Error('Demo işletmesi veya aktif şube bulunamadı.')

  const account = await request(`${coreUrl}/api/v1/credits/account`, { token: businessToken })
  let adjustment = 0
  try {
    if (account.balance < providers.length) {
      adjustment = providers.length + 2
      await request(`${coreUrl}/api/v1/credits/adjustments`, {
        token: platformToken, method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() },
        body: json({ businessId: me.businessId, amount: adjustment, description: 'Webhook simülatörü geçici test kredisi' }),
      })
    }
    for (const provider of providers) await runProvider(provider, businessToken, platformToken, me.businessId, branch.id)
  } finally {
    if (adjustment) {
      await request(`${coreUrl}/api/v1/credits/adjustments`, {
        token: platformToken, method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() },
        body: json({ businessId: me.businessId, amount: -adjustment, description: 'Webhook simülatörü geçici kredi temizliği' }),
      }).catch(() => undefined)
    }
  }
  console.log(`${providers.length} sağlayıcı akışı başarıyla tamamlandı.`)
}

main().catch(error => { console.error(`✗ ${error.message}`); process.exitCode = 1 })
