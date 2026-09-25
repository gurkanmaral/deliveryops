#!/usr/bin/env node

const urls = {
  core: process.env.CORE_API_URL ?? 'http://localhost:5100',
  auth: process.env.AUTH_API_URL ?? 'http://localhost:5200',
  integrations: process.env.INTEGRATIONS_API_URL ?? 'http://localhost:5400',
};

const adminEmail = process.env.PLATFORM_ADMIN_EMAIL ?? 'admin@deliveryops.local';
const adminPassword = process.env.PLATFORM_ADMIN_PASSWORD ?? 'DeliveryOps123!';

if (process.env.ALLOW_NON_LOCAL_SEED !== '1') {
  for (const value of Object.values(urls)) {
    const hostname = new URL(value).hostname;
    if (!['localhost', '127.0.0.1', '::1'].includes(hostname)) {
      throw new Error(`Local seed refused non-local URL ${value}. Set ALLOW_NON_LOCAL_SEED=1 explicitly to override.`);
    }
  }
}

async function request(baseUrl, path, { token, method = 'GET', body, headers = {}, accepted = [] } = {}) {
  const response = await fetch(`${baseUrl}${path}`, {
    method,
    headers: {
      Accept: 'application/json',
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...headers,
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  const data = text ? tryJson(text) : undefined;
  if (!response.ok && !accepted.includes(response.status)) {
    const detail = data?.detail ?? data?.title ?? text ?? response.statusText;
    throw new Error(`${method} ${path} returned ${response.status}: ${detail}`);
  }
  return { status: response.status, data };
}

function tryJson(value) {
  try { return JSON.parse(value); } catch { return value; }
}

const login = await request(urls.auth, '/api/v1/auth/login', {
  method: 'POST',
  body: { email: adminEmail, password: adminPassword },
});
const token = login.data.accessToken;

for (const [name, baseUrl] of Object.entries(urls)) {
  await request(baseUrl, '/health');
  console.log(`✓ ${name} healthy`);
}

async function ensureBusiness(definition) {
  const businesses = (await request(urls.core, '/api/v1/businesses?pageSize=100', { token })).data.items;
  let business = businesses.find(item => item.code === definition.code);
  if (!business) {
    business = (await request(urls.core, '/api/v1/businesses', {
      token, method: 'POST', body: { name: definition.name, code: definition.code },
    })).data;
    console.log(`+ business ${business.name}`);
  }

  const branches = (await request(urls.core, `/api/v1/branches?businessId=${business.id}&pageSize=100`, { token })).data.items;
  const ensuredBranches = [];
  for (const branchDefinition of definition.branches) {
    let branch = branches.find(item => item.name === branchDefinition.name);
    if (!branch) {
      branch = (await request(urls.core, '/api/v1/branches', {
        token, method: 'POST', body: { businessId: business.id, ...branchDefinition },
      })).data;
      console.log(`+ branch ${business.code}/${branch.name}`);
    }
    ensuredBranches.push(branch);
  }

  const couriers = (await request(urls.core, `/api/v1/couriers?businessId=${business.id}&includeInactive=true&pageSize=100`, { token })).data.items;
  const ensuredCouriers = [];
  for (let index = 0; index < definition.couriers.length; index++) {
    const courierDefinition = definition.couriers[index];
    let courier = couriers.find(item => item.phoneNumber === courierDefinition.phoneNumber);
    if (!courier) {
      courier = (await request(urls.core, '/api/v1/couriers', {
        token,
        method: 'POST',
        body: { businessId: business.id, branchId: ensuredBranches[index % ensuredBranches.length].id, ...courierDefinition },
      })).data;
      console.log(`+ courier ${courier.firstName} ${courier.lastName}`);
    }
    ensuredCouriers.push(courier);
  }

  return { ...definition, business, branches: ensuredBranches, couriers: ensuredCouriers };
}

async function ensureUser(users, definition) {
  if (users.some(item => item.email.toLowerCase() === definition.email.toLowerCase())) return;
  await request(urls.auth, '/api/v1/users', { token, method: 'POST', body: definition });
  console.log(`+ user ${definition.email}`);
}

async function ensureShift(courierId, businessId) {
  const active = (await request(urls.core, `/api/v1/shifts/active?businessId=${businessId}`, { token })).data;
  if (!active.some(item => item.courierId === courierId)) {
    await request(urls.core, `/api/v1/shifts/couriers/${courierId}/start`, { token, method: 'POST' });
  }
}

async function transition(order, targetStatus, courierId) {
  const patchStatus = async status => {
    order = (await request(urls.core, `/api/v1/orders/${order.id}/status`, {
      token, method: 'PATCH', body: { status },
    })).data;
  };

  if (order.status === targetStatus) return order;
  if (targetStatus === 7) {
    if (![6, 7, 9].includes(order.status)) {
      order = (await request(urls.core, `/api/v1/orders/${order.id}/cancel`, {
        token, method: 'POST', body: { reason: 'Yerel demo: müşteri iptali' },
      })).data;
    }
    return order;
  }
  if (order.status === 0 && targetStatus > 0) await patchStatus(1);
  if (order.status === 1 && targetStatus >= 2) await patchStatus(2);
  if (order.status === 2 && targetStatus >= 3) {
    order = (await request(urls.core, `/api/v1/orders/${order.id}/courier`, {
      token, method: 'PUT', body: { courierId },
    })).data;
  }
  if (order.status === 3 && [4, 5, 6, 8, 9].includes(targetStatus)) await patchStatus(4);
  if (order.status === 4 && [5, 6, 8].includes(targetStatus)) await patchStatus(5);
  if (order.status === 4 && targetStatus === 9) await patchStatus(9);
  if (order.status === 5 && targetStatus === 6) await patchStatus(6);
  if (order.status === 5 && targetStatus === 8) {
    order = (await request(urls.core, `/api/v1/orders/${order.id}/delivery-failure`, {
      token, method: 'POST', body: { reason: 'Yerel demo: adreste müşteri bulunamadı' },
    })).data;
  }
  if (order.status === 5 && targetStatus === 9) await patchStatus(9);
  return order;
}

async function ensureOrder(context, definition, index) {
  const externalId = `SEED-${context.business.code}-${String(index + 1).padStart(3, '0')}`;
  const branch = context.branches[index % context.branches.length];
  const eligibleCouriers = context.couriers.filter(item => item.branchId === branch.id || item.branchId === null);
  const courier = eligibleCouriers[index % eligibleCouriers.length];
  const existing = (await request(urls.core,
    `/api/v1/orders?businessId=${context.business.id}&search=${encodeURIComponent(externalId)}&pageSize=1`, { token })).data.items[0];
  let order = existing ?? (await request(urls.core, '/api/v1/orders', {
      token,
      method: 'POST',
      headers: { 'Idempotency-Key': `local-seed-order-${externalId}` },
      body: {
        businessId: context.business.id,
        branchId: branch.id,
        externalId,
        customerName: definition.customerName,
        customerPhone: definition.customerPhone,
        deliveryAddress: definition.deliveryAddress,
        totalAmount: definition.totalAmount,
      },
    })).data;
  order = await transition(order, definition.status, courier.id);
  return order;
}

async function ensureConnection(context, provider, name, authMode, adapterVersion) {
  const connections = (await request(urls.integrations, '/api/v1/integrations', { token })).data;
  if (connections.some(item => item.businessId === context.business.id && item.name === name)) return;
  await request(urls.integrations, '/api/v1/integrations', {
    token,
    method: 'POST',
    body: {
      businessId: context.business.id,
      branchId: context.branches[0].id,
      provider,
      name,
      authMode,
      adapterVersion,
    },
  });
  console.log(`+ integration ${context.business.code}/${name}`);
}

const definitions = [
  {
    code: 'DEMO-01',
    name: 'Demo Restoran',
    admin: { email: 'business@demo.local', password: 'BusinessDemo123!', firstName: 'Demo', lastName: 'İşletme' },
    staff: { email: 'staff@demo.local', password: 'StaffDemo123!', firstName: 'Demo', lastName: 'Personel' },
    branches: [
      { name: 'Merkez Şube', address: 'Kadıköy, İstanbul', latitude: 40.9909, longitude: 29.0283 },
      { name: 'Moda Şube', address: 'Moda, Kadıköy, İstanbul', latitude: 40.9826, longitude: 29.0257 },
    ],
    couriers: [
      { firstName: 'Demo', lastName: 'Kurye', phoneNumber: '+905550000001' },
      { firstName: 'Ayşe', lastName: 'Yılmaz', phoneNumber: '+905550000002' },
      { firstName: 'Mehmet', lastName: 'Kaya', phoneNumber: '+905550000003' },
    ],
  },
  {
    code: 'DEMO-02',
    name: 'Mavi Fırın',
    admin: { email: 'business@mavifirin.local', password: 'MaviFirin123!', firstName: 'Mavi', lastName: 'Yönetici' },
    staff: { email: 'staff@mavifirin.local', password: 'MaviStaff123!', firstName: 'Mavi', lastName: 'Personel' },
    branches: [
      { name: 'Beşiktaş Şube', address: 'Sinanpaşa, Beşiktaş, İstanbul', latitude: 41.0434, longitude: 29.0073 },
      { name: 'Levent Şube', address: 'Levent, Beşiktaş, İstanbul', latitude: 41.0813, longitude: 29.0117 },
    ],
    couriers: [
      { firstName: 'Can', lastName: 'Demir', phoneNumber: '+905550000101' },
      { firstName: 'Elif', lastName: 'Şahin', phoneNumber: '+905550000102' },
    ],
  },
];

const contexts = [];
for (const definition of definitions) contexts.push(await ensureBusiness(definition));

const users = (await request(urls.auth, '/api/v1/users', { token })).data;
for (const context of contexts) {
  await ensureUser(users, { ...context.admin, role: 'BusinessAdmin', businessId: context.business.id, branchId: null, courierId: null });
  await ensureUser(users, { ...context.staff, role: 'BusinessStaff', businessId: context.business.id, branchId: null, courierId: null });
  for (let index = 0; index < context.couriers.length; index++) {
    const courier = context.couriers[index];
    await ensureUser(users, {
      email: `courier${context.business.code === 'DEMO-01' && index === 0 ? '' : `-${context.business.code.toLowerCase()}-${index + 1}`}@demo.local`,
      password: 'CourierDemo123!',
      firstName: courier.firstName,
      lastName: courier.lastName,
      role: 'Courier',
      businessId: context.business.id,
      branchId: courier.branchId,
      courierId: courier.id,
    });
  }

  await request(urls.core, '/api/v1/credits/top-up', {
    token,
    method: 'POST',
    headers: { 'Idempotency-Key': context.business.code === 'DEMO-01'
      ? '10000000-0000-4000-8000-000000000001'
      : '10000000-0000-4000-8000-000000000002' },
      body: { businessId: context.business.id, packageCode: 'CREDIT_1000', description: 'Yerel demo başlangıç kredisi' },
  });
  await request(urls.core, '/api/v1/dispatch/settings', {
    token,
    method: 'PUT',
    body: {
      businessId: context.business.id,
      autoConfirmOrders: false,
      autoAssignCouriers: false,
      allowCourierSelfClaim: true,
      preferBranchCouriers: true,
      maxActiveOrdersPerCourier: 5,
      requireFreshLocation: false,
      locationFreshnessMinutes: 10,
      assignmentRadiusKm: 15,
    },
  });
  await request(urls.core, '/api/v1/billing/settings', {
    token,
    method: 'PUT',
    body: { businessId: context.business.id, feePerDeliveredOrder: 18.5, commissionRatePercent: 2.5, feePerReturnedOrder: 7.5, taxRatePercent: 20 },
  });

  const latestLocations = (await request(urls.core, `/api/v1/locations/latest?businessId=${context.business.id}`, { token })).data;
  for (let courierIndex = 0; courierIndex < context.couriers.length; courierIndex++) {
    const courier = context.couriers[courierIndex];
    await ensureShift(courier.id, context.business.id);
    const branch = context.branches.find(item => item.id === courier.branchId) ?? context.branches[0];
    if (!latestLocations.some(item => item.courierId === courier.id && !item.isStale)) {
      const offset = (courierIndex + 1) * 0.0015;
      await request(urls.core, `/api/v1/locations/couriers/${courier.id}`, {
        token,
        method: 'POST',
        body: {
          latitude: branch.latitude + offset,
          longitude: branch.longitude - offset,
          accuracyMeters: 12,
          speedMetersPerSecond: 0,
          headingDegrees: 90,
          recordedAtUtc: new Date().toISOString(),
        },
      });
    }
  }

  await ensureConnection(context, 0, 'Yemeksepeti Demo', 2, 'yemeksepeti-partner-v2');
  await ensureConnection(context, 1, 'Getir Demo', 1, 'canonical-v1');
  await ensureConnection(context, 2, 'POS Köprüsü Demo', 0, 'canonical-v1');
}

const sampleOrders = [
  { status: 0, customerName: 'Zeynep Acar', customerPhone: '05551000001', deliveryAddress: 'Caferağa Mah., Kadıköy', totalAmount: 285.5 },
  { status: 1, customerName: 'Mert Öztürk', customerPhone: '05551000002', deliveryAddress: 'Fenerbahçe Mah., Kadıköy', totalAmount: 410 },
  { status: 2, customerName: 'Selin Arslan', customerPhone: '05551000003', deliveryAddress: 'Koşuyolu Mah., Kadıköy', totalAmount: 192.75 },
  { status: 3, customerName: 'Burak Çelik', customerPhone: '05551000004', deliveryAddress: 'Acıbadem Mah., Kadıköy', totalAmount: 330 },
  { status: 5, customerName: 'Deniz Koç', customerPhone: '05551000005', deliveryAddress: 'Erenköy Mah., Kadıköy', totalAmount: 515.25 },
  { status: 6, customerName: 'Ece Yıldız', customerPhone: '05551000006', deliveryAddress: 'Suadiye Mah., Kadıköy', totalAmount: 248 },
  { status: 7, customerName: 'Kerem Aksoy', customerPhone: '05551000007', deliveryAddress: 'Bostancı Mah., Kadıköy', totalAmount: 176.5 },
  { status: 8, customerName: 'Derya Kurt', customerPhone: '05551000008', deliveryAddress: 'Göztepe Mah., Kadıköy', totalAmount: 364 },
  { status: 9, customerName: 'Ozan Işık', customerPhone: '05551000009', deliveryAddress: 'Caddebostan Mah., Kadıköy', totalAmount: 225 },
];

for (const context of contexts) {
  const count = context.business.code === 'DEMO-01' ? sampleOrders.length : 5;
  for (let index = 0; index < count; index++) await ensureOrder(context, sampleOrders[index], index);
  console.log(`✓ ${context.business.name}: ${count} sample orders ready`);
}

console.log('\nLocal sample data is ready.');
console.log('Admin: admin@deliveryops.local / DeliveryOps123!');
console.log('Business: business@demo.local / BusinessDemo123!');
console.log('Courier: courier@demo.local / CourierDemo123!');
