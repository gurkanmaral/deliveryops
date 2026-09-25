# DeliveryOps Admin Web

Feature-first klasör yapısına sahip React yönetim paneli. Uygulama sağlayıcıları `src/app`, ortak API yardımcıları `src/shared`, iş modülleri ise `src/features` altında tutulur.

```bash
cp .env.example .env
npm install
npm run dev
```

Kalite kontrolleri:

```bash
npm run lint
npm run build
npx playwright install chromium
npm run test:e2e
```

E2E testleri local Core (`5100`), Auth (`5200`) ve Vite (`5173`) servislerini kullanır. Vite çalışmıyorsa Playwright otomatik başlatır; API'lerin önceden ayakta olması gerekir. Görsel test arayüzü için `npm run test:e2e:ui` kullanılabilir.
