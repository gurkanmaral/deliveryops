# DeliveryOps

Çok işletmeli sipariş ve kurye operasyon platformunun ilk geliştirme iskeleti.

## Yapı

- `apps/admin-web`: React, TypeScript, Vite ve TanStack Query tabanlı yönetim paneli.
- `apps/courier-mobile`: Expo SDK 57 ve React Native tabanlı kurye uygulaması.
- `apps/pos-bridge`: .NET 8 çekirdeği, CLI ve Windows WPF arayüzünden oluşan POS sipariş köprüsü.
- `backend/src/Services/Core`: Domain, Queries, Handlers, Infrastructure ve API katmanlarına ayrılmış ana operasyon servisi.
- `backend/src/Services/Auth`: Kimlik ve yetki servisi iskeleti.
- `backend/src/Services/Notifications`: Cihaz kayıtları, Expo push gönderimi ve receipt takibi yapan bildirim servisi.
- `backend/src/Services/Integrations`: POS ve pazar yeri entegrasyon servisi iskeleti.
- `compose.yml`: PostGIS destekli PostgreSQL, Redis ve RabbitMQ geliştirme altyapısı.

## Gereksinimler

- .NET SDK 8.0
- Node.js 22.13+
- Docker Desktop

## Yerelde Docker ile çalıştırma

Dört API, migration job'ı, PostgreSQL, Redis ve RabbitMQ tek komutla Docker içinde başlatılır:

```bash
make stack-build
```

Bu komut migration'ları API'lerden önce uygular ve servisler sağlıklı olmadan tamamlanmaz. Sonraki açılışlarda image'ları tekrar derlemeden `make stack`, tüm stack'i durdurmak için `make stack-down` kullanılabilir. API'ler sırasıyla `5100`, `5200`, `5300` ve `5400`; RabbitMQ yönetim ekranı ise çakışmaları önlemek için `15673` portundadır. Varsayılan olarak tüm yayınlanan portlar yalnızca `127.0.0.1` üzerinde dinler.

Daha önce API'leri doğrudan macOS üzerinde çalıştırarak şifreli sipariş/entegrasyon verisi oluşturduysanız, `~/.aspnet/DataProtection-Keys` anahtar halkasını ilk Docker geçişinde `deliveryops-core-keys` ve `deliveryops-integrations-keys` volume'larına taşımanız gerekir. Bu çalışma alanındaki mevcut yerel anahtarlar taşınmıştır; volume'ları silmeyin.

## Yerelde süreç olarak çalıştırma

```bash
docker compose up -d
dotnet restore backend/DeliveryOps.sln
cd backend
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/Services/Core/DeliveryOps.Core.Infrastructure --startup-project src/Services/Core/DeliveryOps.Core.Api
dotnet tool run dotnet-ef database update --project src/Services/Notifications/DeliveryOps.Notifications.Api --startup-project src/Services/Notifications/DeliveryOps.Notifications.Api
dotnet run --project src/Services/Core/DeliveryOps.Core.Api --urls http://localhost:5100
```

## Yerel testler

Çalışan dört API ve admin panel üzerinde rol/yetki, temel listeleme, kurye mobil oturumu ve push cihazı kayıt-silme akışını hızlıca kontrol etmek için:

```bash
make smoke
```

Backend ve POS unit/integration testleri, admin panel lint/build, Playwright tarayıcı E2E testleri, mobil Expo Doctor/typecheck/lint/web export ve son olarak smoke testinin tamamını çalıştırmak için:

```bash
make test
```

Yalnızca Chromium üzerinde panel E2E testlerini çalıştırmak için dört API ayaktayken:

```bash
make e2e
```

Yalnızca kod kalite kontrollerini çalıştırıp ayakta servis smoke testini atlamak için:

```bash
make check
```

İlk kurulumda altyapı ve Core migration komutları ayrıca kısaltılmıştır:

```bash
make infra
make migrate
```

Dört API çalışırken iki işletme, dört şube, beş kurye, kullanıcı rolleri, kredi,
tarife, canlı vardiya/konum, entegrasyon bağlantıları ve farklı durumlarda 14
siparişten oluşan tekrar çalıştırılabilir yerel veri setini hazırlamak için:

```bash
make seed
```

Seed gerçek API uçlarını kullanır ve sabit işletme kodları, kullanıcı e-postaları,
dış sipariş numaraları ve idempotency anahtarları sayesinde tekrar çalıştırıldığında
işletme, kullanıcı, bağlantı veya siparişleri çoğaltmaz. Güvenlik amacıyla varsayılan
olarak yalnızca localhost adreslerinde çalışır.

Smoke testi local geliştirme verisini bozmaz. Oluşturduğu geçici push cihaz kaydını ve mobil refresh token'ı test sonunda temizler. E2E sipariş testi dispatch ayarı, kurye vardiyası ve krediyi eski haline getirir; oluşturduğu siparişi iptal edip krediyi iade eder. Mutabakat belgesi değişmez kayıt olduğu için ilk E2E koşusu mevcut dönem için bir kesinleşmiş local test belgesi bırakabilir, sonraki koşular bu belgeyi yeniden kullanır. Adresler gerektiğinde `CORE_API_URL`, `AUTH_API_URL`, `NOTIFICATIONS_API_URL`, `INTEGRATIONS_API_URL` ve `ADMIN_WEB_URL` environment değişkenleriyle override edilebilir.

Auth API için ayrı bir terminalde:

```bash
cd backend
dotnet run --project src/Services/Auth/DeliveryOps.Auth.Api --urls http://localhost:5200
```

Notifications API için ayrı bir terminalde:

```bash
cd backend
dotnet run --project src/Services/Notifications/DeliveryOps.Notifications.Api --urls http://localhost:5300
```

Integrations API için ayrı bir terminalde:

```bash
cd backend
dotnet run --project src/Services/Integrations/DeliveryOps.Integrations.Api --urls http://localhost:5400
```

Fiziksel telefonda kurye uygulamasını çalıştırmak için API adreslerinde `localhost` yerine `0.0.0.0`, mobil `.env.local` dosyasında ise bilgisayarın yerel ağ IP adresi kullanılmalıdır.

Ayrı bir terminalde:

```bash
cd apps/admin-web
cp .env.example .env
npm install
npm run dev
```

Panel `http://localhost:5173`, Core Swagger `http://localhost:5100/swagger`, Auth Swagger `http://localhost:5200/swagger`, Notifications Swagger `http://localhost:5300/swagger`, Integrations Swagger ise `http://localhost:5400/swagger` adresinde açılır.

## Production Docker dağıtımı

Örnek ortam dosyasını kaynak kontrolü dışında güvenli bir konuma kopyalayın, güçlü secret'ları ve gerçek origin adreslerini girin. RSA anahtarlarını README'deki güvenlik bölümünde gösterildiği biçimde üretin:

```bash
cp .env.production.example /secure/path/deliveryops.production.env
docker compose --env-file /secure/path/deliveryops.production.env \
  -f compose.yml -f compose.production.yml \
  --profile application up -d --build --wait
```

Production override eksik secret, RSA anahtarı veya origin olduğunda başlamaz. API portları yalnızca loopback'e açılır; internet trafiği TLS sonlandıran bir reverse proxy üzerinden yönlendirilmelidir. PostgreSQL, Redis, RabbitMQ ve Data Protection volume'ları düzenli yedeklenmeli; `.env` dosyası image içine kopyalanmamalı veya repoya eklenmemelidir.

Kurye rota gruplaması production ortamında Google Routes API `Compute Route Matrix` ile `TWO_WHEELER` yol mesafesini doğrular. Google Cloud projesinde Routes API ve faturalandırma etkinleştirilmeli; anahtar yalnızca sunucu IP'leri ve Routes API ile sınırlandırılmalıdır:

```bash
DELIVERYOPS_ROAD_ROUTING_ENABLED=true
DELIVERYOPS_GOOGLE_ROUTES_API_KEY=restricted-server-side-key
```

Kuş uçuşu mesafe yalnızca maliyetsiz ön eleme için kullanılır. Nihai gruplama gerçek motosiklet yol mesafesiyle yapılır. API anahtarı eksikse, sağlayıcı hata verirse, rota bulunamazsa veya koordinat yaklaşık ise sistem güvenli biçimde otomatik gruplama yapmaz; normal kurye kapasite sıralamasına döner. Anahtar istemci uygulamalarına gönderilmez ve rota sonuçları kalıcı olarak önbelleğe alınmaz.

Kurye uygulaması:

```bash
cd apps/courier-mobile
nvm use
npm install
cp .env.example .env.local
npm start
```

Yerel sağlayıcı webhook ve iOS Simulator uçtan uca testleri:

```bash
make webhook-e2e       # Yemeksepeti + Getir; sipariş ve idempotency doğrulaması
make mobile-e2e        # giriş, vardiya, self-claim, teslimat ve çıkış
```

Webhook simülatörü her koşuda geçici bir bağlantı oluşturur, sağlayıcı payload'ını imzalı API anahtarıyla gönderir, Core siparişini doğrular, aynı olayı tekrar göndererek idempotency'yi sınar ve bağlantıyı pasife alır. Getir resmi sözleşmesi bağlanana kadar mevcut `canonical-v1`, Yemeksepeti için `yemeksepeti-partner-v2` adaptörü kullanılır.

Expo Go ön plan konumu için yeterlidir; arka plan konumu `expo-dev-client` içeren development build gerektirir. Çevrimdışı konumlar kurye hesabına bağlı olarak cihazda sıralı biçimde kuyruklanır, uygulama ön plana geldiğinde yeniden gönderilir ve çıkışta temizlenir.
Push notification için EAS project ID, Android FCM ve iOS APNs kimlik bilgilerinin EAS tarafında yapılandırılması gerekir. Mobil uygulama token değişimini dinler; yeni token'ı kaydedip eskisini pasifleştirir. Ön planda gelen bildirim sipariş sorgularını yeniler, bildirime dokunmak ilgili sipariş ekranını açar ve çıkış cihaz kaydını kapatır. Development build'deki Profil ekranından EAS bağlantısı olmadan yerel bildirim akışı sınanabilir.

Yerel Redis bu çalışma alanında `6380` portuna yayınlanır. Core API son kurye konumunu Redis'te, konum geçmişini ise PostGIS'te saklar; `/hubs/operations` SignalR hub'ı paneli anlık olarak günceller.

Yerel geliştirme yöneticisi:

- E-posta: `admin@deliveryops.local`
- Şifre: `DeliveryOps123!`

Yerel işletme paneli yöneticisi:

- E-posta: `business@demo.local`
- Şifre: `BusinessDemo123!`
- İşletme: `Demo Restoran`

Yerel işletme personeli:

- E-posta: `staff@demo.local`
- Şifre: `StaffDemo123!`

Yerel kurye test hesabı:

- E-posta: `courier@demo.local`
- Şifre: `CourierDemo123!`

Bu hesap ve imzalama anahtarı yalnızca `Development` yapılandırmasındadır. Canlı ortam değerleri secret manager veya environment variable üzerinden verilmelidir.

## Mimari kararlar

Core servisindeki `Domain / Queries / Handlers / Infrastructure / API` ayrımı, yerel `core-api` projesindeki CQRS yaklaşımını takip eder. İşletme, şube, kurye ve sipariş ilk aşamada aynı servis içindedir; bağımsız ölçekleme ihtiyacı oluştuğunda modüller servis olarak ayrılabilir. Auth, Notifications ve Integrations baştan ayrı dağıtım sınırları olarak tutulmuştur.

Core katman yönü otomatik mimari testleriyle korunur: Domain dış Core katmanlarını,
Queries Handlers/Infrastructure/API katmanlarını, Handlers Infrastructure/API
katmanlarını ve Infrastructure API katmanını referanslayamaz. Production ortamında
servis başlangıcında migration çalıştırılmaz; migration dağıtım adımında ayrıca
uygulanır. `Database__AutoMigrate=true` yalnızca local Development kolaylığı içindir.

Refresh token tarayıcıda `HttpOnly` cookie olarak tutulur. Access token bellek içinde saklanır; sayfa yenilendiğinde refresh endpoint'i üzerinden yeniden üretilir.

Admin web aynı uygulama içinde rol bazlı görünümler sunar. Platform yöneticisi tüm işletmeleri yönetebilir; işletme yöneticisi ise işletme seçicileri olmadan yalnızca kendi işletmesinin dashboard, şube, kurye, sipariş ve entegrasyon verilerini görür. İşletme yöneticisi kendi bağlantılarını yönetebilir, işletme personeli entegrasyonları salt okunur izler. Hem API sorguları JWT içindeki `business_id` ile tenant kapsamında filtrelenir hem de panel rotaları ve yazma kontrolleri izinlerle korunur.

`Kullanıcılar` ekranında platform yöneticisi işletme yöneticisi veya personel hesabı oluşturabilir. İşletme yöneticisi yalnızca kendi tenant'ında personel hesabı oluşturabilir, aktifleştirebilir veya pasife alabilir; kendi hesabı ve daha yüksek yetkili roller korunur.

## Sipariş operasyon akışı

Sipariş akışı `Yeni → Onaylandı → Kurye bekliyor → Atandı → Teslim alındı → Yolda → Teslim edildi` durum makinesiyle korunur. İptal, teslim edilememe, tekrar deneme ve iade geçişleri ayrıca tanımlıdır. Kurye yalnızca kendi siparişlerini ve üstlenilebilir kuyruğu görebilir.

- `GET /api/v1/orders/available`: kuryenin üstlenebileceği paketler
- `POST /api/v1/orders/{id}/claim`: paketi atomik olarak üstlenme
- `PUT /api/v1/orders/{id}/courier`: operasyon panelinden manuel atama
- `PATCH /api/v1/orders/{id}/status`: izin verilen bir sonraki duruma geçiş
- `POST /api/v1/orders/{id}/delivery-failure`: gerekçeli teslimat hatası
- `POST /api/v1/orders/{id}/cancel`: gerekçeli iptal

Atama ve durum değişiklikleri PostgreSQL `xmin` optimistic concurrency token'ıyla korunur. Aynı paket için yarışan ikinci işlem `409 Conflict` alır; başarılı değişiklikler SignalR üzerinden panel ve ilgili kuryeye yayınlanır.

## Bildirim akışı

Sipariş `Kurye bekliyor` veya `Atandı` durumuna geçtiğinde Core aynı veritabanı transaction'ı içinde `notification_outbox` kaydı oluşturur. Arka plan dispatcher bu kaydı Notifications API'ye idempotent event olarak iletir ve geçici hatalarda üstel gecikmeyle yeniden dener. Notifications API:

- Kurye JWT claim'leriyle cihaz token'ı kaydeder ve pasifleştirir.
- Havuz paketini işletme/şube kuryelerine, atanmış paketi ilgili kuryeye gönderir.
- Expo push isteklerini en fazla 100 mesajlık gruplara böler.
- Push ticket receipt sonuçlarını takip eder ve `DeviceNotRegistered` token'larını pasifleştirir.

Canlı ortamda internal API key'ler, JWT anahtarları ve veritabanı şifreleri secret manager üzerinden verilmelidir. Auth API access token'ı RSA private key ile imzalar; Core, Notifications ve Integrations API'leri yalnızca public key ile doğrular. Böylece resource API ele geçirilse dahi yeni kullanıcı token'ı üretilemez. PEM değerleri doğrudan veya base64 olarak verilebilir:

```bash
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out jwt-private.pem
openssl rsa -pubout -in jwt-private.pem -out jwt-public.pem

# Auth API secret'ı
Jwt__PrivateKeyPemBase64="$(base64 < jwt-private.pem | tr -d '\n')"

# Core, Notifications ve Integrations API secret'ı
Jwt__PublicKeyPemBase64="$(base64 < jwt-public.pem | tr -d '\n')"
```

Private key hiçbir resource API'ye veya kaynak kontrolüne verilmemelidir. Simetrik `Jwt:SigningKey` desteği yalnızca local `Development` ortamında kolay geliştirme amacıyla açıktır; production başlangıcı RSA anahtarı yoksa güvenli biçimde durur.

Anahtar rotasyonu kesintisiz yapılabilir:

1. Yeni public key'i tüm resource API'lere `Jwt__PublicKeyPemBase64` olarak dağıtın.
2. Eski public key'i geçiş süresince `Jwt__PreviousPublicKeysPemBase64__0` altında tutun.
3. Auth API'ye yeni private key'i verin ve yeni token üretimini başlatın.
4. En uzun access-token ömrü ve güvenlik payı geçtikten sonra eski public key'i kaldırın.

Token header'ındaki `kid`, public key'in SHA-256 parmak izinden üretildiği için doğrulayıcı doğru anahtarı seçebilir. Önceki private key hiçbir serviste tutulmaz.

Siparişlerdeki müşteri adı, telefon ve teslimat adresi Core veritabanında ASP.NET Data Protection ile uygulama seviyesinde şifrelenir. Ad/telefon araması düz metin indeks yerine HMAC-SHA256 blind-index token'larıyla yapılır. Tamamlanmış, iptal edilmiş veya iade edilmiş siparişlerin PII alanları varsayılan 90 gün sonra anonimleştirilir; süre `CoreRetention__OrderPiiDays` ile ayarlanır. Canlı ortamda aşağıdaki değerler secret manager ve kalıcı bir key-ring volume üzerinden verilmelidir:

```bash
# En az 32 rastgele byte; bu anahtar loglanmamalı ve kaynak kontrolüne girmemelidir.
OrderPii__SearchKey="$(openssl rand -base64 48)"

# Tüm Core API replikalarının eriştiği, yedeklenen kalıcı dizin.
OrderPii__KeyRingPath="/var/lib/deliveryops/data-protection-keys"
```

`OrderPii__SearchKey` eksik veya kısa olduğunda production başlangıcı güvenli biçimde durur. Data Protection key-ring kaybolursa mevcut PII çözülemez; bu dizin veritabanı yedeğiyle birlikte korunmalı ve çoklu Core replikalarında paylaşılmalıdır. Search key değişimi mevcut blind-index'lerin yeniden üretilmesini gerektirdiği için plansız değiştirilmemelidir.

Backend integration testleri Docker üzerinde geçici bir PostGIS/PostgreSQL container'ı başlatır. Böylece migration, partial unique index ve PostgreSQL `xmin` concurrency davranışları gerçek veritabanı motorunda doğrulanır. `dotnet test backend/DeliveryOps.sln` çalıştırılmadan önce Docker'ın açık olması gerekir.

Internal API key rotasyonunda alıcı servis önce yeni anahtarı geçici kabul listesine eklemelidir. Gönderen servis aktif anahtara geçirildikten sonra alıcıda yeni anahtar aktif, eski anahtar previous olarak tutulur ve geçiş tamamlanınca kaldırılır. Kullanılan config yolları:

- Core: `Integrations__InternalApiKey`, `Integrations__PreviousInternalApiKeys__0`
- Integrations: `InternalApiKey`, `PreviousInternalApiKeys__0`
- Notifications: `InternalApi__Key`, `InternalApi__PreviousKeys__0`

Gönderen `HttpClient` her zaman yalnızca aktif anahtarı yollar; previous listesi yalnızca gelen çağrı doğrulamasında kullanılır.

Core → Integrations ve Core → Notifications outbox'ları HTTP cevaplarını sınıflandırır: `408`, `409`, `425`, `429` ve `5xx` cevapları geçici kabul edilir; diğer `4xx` cevapları kalıcı hatadır. Geçici hatalar varsayılan olarak en fazla sekiz kez denenir ve ardından dead-letter'a taşınır. Limitler `IntegrationOutbox__MaxAttempts` ve `NotificationOutbox__MaxAttempts` ile ayarlanabilir. Yetkili kullanıcılar paneldeki **Hata Kuyrukları** ekranından tenant izolasyonu uygulanmış kayıtları görebilir ve sorun giderildikten sonra manuel retry başlatabilir. API karşılıkları `GET/POST /api/v1/operations/integration-outbox` ve `GET/POST /api/v1/operations/notification-outbox` yollarıdır. Manuel retry işlemleri audit log'a yazılır.

## Entegrasyon sipariş akışı

Platform yöneticisi tüm işletmeler, işletme yöneticisi ise yalnızca kendi işletmesi için paneldeki `Entegrasyonlar` ekranından şube ve sağlayıcı bağlantısı oluşturur. İşletme personeli bağlantı ve olayları salt okunur izleyebilir. API, webhook secret'ını yalnızca oluşturma veya yenileme cevabında gösterir. API anahtarı modunda `X-DeliveryOps-Key`, HMAC modunda `X-DeliveryOps-Timestamp` ve `X-DeliveryOps-Signature`, statik yetkilendirme modunda ise tam `Authorization` başlığı doğrulanır. HMAC doğrulama kopyası ve sağlayıcı secret'ları ASP.NET Data Protection ile şifreli saklanır.

POS ve sağlayıcı sözleşmesi henüz alınmamış entegrasyonlar, bağlantıya ait URL'ye aşağıdaki normalize edilmiş `canonical-v1` sözleşmesini gönderir (`eventId` ve `eventType` verilmezse sipariş numarası ve `order.created` kullanılır):

```json
{
  "eventId": "evt-2026-0001",
  "eventType": "order.created",
  "externalOrderId": "POS-2026-0001",
  "customerName": "Test Müşteri",
  "customerPhone": "05550000000",
  "deliveryAddress": "Kadıköy, İstanbul",
  "totalAmount": 245.50
}
```

Inbound olayları `integrations.inbound_order_events` tablosunda ham payload, SHA-256 özeti, normalize snapshot ve adaptör sürümüyle kalıcı olarak izlenir. Webhook kaydedildiğinde `202 Accepted` döner; worker Core aktarımını arka planda yapar, geçici hatalarda üstel gecikmeyle en fazla beş kez dener ve sonrasında olayı dead-letter durumuna taşır. Panel olayın hatasını ve sonraki deneme zamanını gösterir; başarısız olay manuel olarak yeniden işlenebilir. Aynı olay numarasının farklı payload ile gönderilmesi reddedilir. Core API çağrıları ayrı bir internal API-key authentication scheme ile korunur. Dış sipariş benzersizliği işletme + kaynak + dış sipariş numarası bileşimindedir.

Webhook dönüşümü `IOrderProviderAdapter` arayüzüyle Core sipariş modelinden ayrılmıştır. `canonical-v1` genel adaptörüne ek olarak `yemeksepeti-partner-v2`, Yemeksepeti Partner API sipariş bildirimindeki `order_id`, `status`, müşteri, teslimat adresi, ödeme ve sistem zamanı alanlarını normalize eder. `RECEIVED`, `READY_FOR_PICKUP`, `DISPATCHED`, `CANCELLED` ve `DELIVERED` durumları kabul edilir. Yemeksepeti bağlantıları panelde varsayılan olarak statik `Authorization` doğrulaması ve bu adaptörle oluşturulur.

HMAC imzası `HMAC-SHA256(secret, timestamp + "." + rawBody)` biçimindedir ve beş dakikalık replay penceresi uygulanır.

Webhook secret yenilendiğinde eski secret varsayılan olarak 24 saat daha kabul edilir. Panel yeni secret ile birlikte eski anahtarın son geçerlilik zamanını gösterir; süre dolan eski hash ve şifreli secret retention worker tarafından silinir. Geçiş süresi `IntegrationSecurity:WebhookPreviousSecretGraceMinutes`, bağlantı başına webhook limiti `IntegrationSecurity:WebhookRateLimitPerMinute` ile ayarlanır. Varsayılan limit dakikada 300 istek, maksimum request body boyutu 1 MB'tır.

Birden fazla Integrations API instance'ı kullanılan canlı ortamda Data Protection key ring bütün instance'lar arasında paylaşılmalı ve kalıcı bir secret store ile korunmalıdır; aksi halde başka instance tarafından oluşturulan HMAC secret çözülemez.

Bu sözleşme Windows POS uygulaması için doğrudan kullanılabilir. Yemeksepeti inbound adaptörü resmî Partner API v2 sözleşmesine göre hazırdır. Getir adaptörü, kurumsal erişimle sağlanan güncel payload ve doğrulama sözleşmesi alınana kadar `canonical-v1` olarak tutulur; doğrulanmamış bir sağlayıcı sözleşmesi varsayılmaz.

### Yemeksepeti durum senkronizasyonu

Yemeksepeti bağlantısındaki `OAuth ayarla` işlemiyle sandbox veya production ortamı, `chain_id`, `client_id` ve `client_secret` kaydedilir. Client bilgileri Data Protection ile şifrelenir; API yalnızca bilgilerin yapılandırılmış olup olmadığını döndürür ve secret'ı geri göstermez. OAuth token'ı `expires_in` süresine göre bellekte tekrar kullanılır.

`Bağlantıyı test et` işlemi token önbelleğini atlayarak seçilen ortamdaki `/v2/oauth/token` adresinden yeni bir token ister. Son test zamanı, başarılı/başarısız sonucu, güvenli kullanıcı mesajı ve başarılı token'ın bitiş zamanı bağlantı üzerinde saklanır. Credential veya ortam değiştirildiğinde eski sağlık sonucu temizlenir. Sağlayıcının hata gövdesi, access token ve client secret API cevabına ya da panele aktarılmaz.

Aktif ve credential'ı tanımlı Yemeksepeti bağlantıları varsayılan olarak 15 dakikada bir arka planda kontrol edilir. Kontrol aralığı `IntegrationHealthChecks` ayarlarından değiştirilebilir. İki ardışık başarısızlıktan sonra işletme için tekil bir kritik `IntegrationConnectionUnavailable` operasyon alarmı açılır ve SignalR üzerinden panelin bildirim merkezine yansıtılır. Geçici tek hata alarm üretmez; bağlantı yeniden doğrulandığında mevcut alarm otomatik çözülür. Worker, birden fazla instance aynı bağlantıyı seçse bile atomik claim kullanarak aynı periyotta yinelenen OAuth isteğini önler.

Entegrasyon sayfasındaki monitoring alanı son 24 saat, 7 gün veya 30 gün için gelen/giden olay hacmini, başarı oranını, retry ve dead-letter sayılarını, ortalama işleme süresini, bağlantı bazlı son başarılı iletişimi ve sağlayıcı dağılımını gösterir. Her OAuth kontrolü `integrations.health_checks` tablosunda geçmiş kaydı oluşturur; kullanılabilirlik oranı bu gerçek örneklerden hesaplanır. İşletme kullanıcılarının monitoring sorgusu JWT içindeki `business_id` ile sınırlandırılır.

`IntegrationRetention` ayarı varsayılan olarak 90 gündür. Günlük çalışan retention worker yalnızca başarıyla tamamlanmış eski inbound/outbound olaylarını ve eski sağlık örneklerini siler; başarısız, bekleyen ve dead-letter kayıtları inceleme için korunur.

Core sipariş durum değişikliğini aynı transaction içindeki `integration_outbox` tablosuna yazar. Dispatcher olayı internal API-key ile Integrations API'ye iletir. Integrations API, kaynak webhook olayındaki `transport_type` ve orijinal `items` alanını kullanarak aşağıdaki eşlemeyi uygular:

- `LOGISTICS_DELIVERY` siparişi `WaitingForCourier` olduğunda `READY_FOR_PICKUP`
- Diğer teslimat tiplerinde sipariş `PickedUp` olduğunda `DISPATCHED`
- Uygun aşamadaki iptal işleminde `CANCELLED`

Gönderimler `integrations.outbound_order_events` tablosunda idempotent olarak tutulur. Worker başarısız istekleri üstel gecikmeyle beş kez dener ve ardından dead-letter durumuna taşır. Admin paneli OAuth durumunu, gönderim geçmişini, son hatayı ve manuel yeniden gönderme işlemini gösterir. Sağlayıcı credential'ı girilmeden gerçek dış API çağrısı yapılmaz; olay hata ve retry bilgisiyle izlenebilir kalır.

### Inbound sipariş yaşam döngüsü

Yemeksepeti `RECEIVED` olayı yeni Core siparişi oluşturur. Aynı sipariş için daha sonra gelen olaylar yeni sipariş oluşturmaya çalışmaz; mevcut siparişi aşağıdaki şekilde günceller:

- `READY_FOR_PICKUP`: `New → Confirmed → WaitingForCourier` geçişlerini uygular; sipariş daha ilerideyse idempotent no-op olur.
- `DISPATCHED`: yalnızca uygun `PickedUp → OnTheWay` geçişini uygular; erken gelirse retry kuyruğunda bekler.
- `DELIVERED`: yalnızca uygun `OnTheWay → Delivered` geçişini uygular.
- `CANCELLED`: teslimat başlamadan siparişi iptal eder, sağlayıcı nedenini saklar ve tüketilen 1 krediyi yalnızca bir kez iade eder.

İşlenen sağlayıcı olayları `provider_order_event_receipts` tablosunda işletme + kaynak + dış olay numarasıyla değişmez ve benzersiz olarak kaydedilir. Böylece cevap kaybolsa veya aynı webhook tekrar gelse bile durum geçişi ve kredi iadesi tekrarlanmaz. Sırası bozuk fakat daha sonra uygulanabilecek olaylar retry edilir; tamamlanmış/iptal edilmiş siparişlere ait eski olaylar güvenli no-op olarak kaydedilir. Sağlayıcıdan gelen lifecycle olayının Core outbox üzerinden tekrar sağlayıcıya gönderilmesi echo kontrolüyle engellenir.

## Doğrulama

```bash
dotnet test backend/DeliveryOps.sln
cd apps/admin-web
npm run build
npm run lint
cd ../courier-mobile
npm run typecheck
npm run doctor
cd ../pos-bridge
dotnet test DeliveryOps.PosBridge.sln
dotnet build src/DeliveryOps.PosBridge.Wpf/DeliveryOps.PosBridge.Wpf.csproj
```
