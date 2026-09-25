# DeliveryOps Kurye

Expo SDK 57, React Native ve Expo Router tabanlı kurye uygulaması.

## Özellikler

- Kurye hesabına özel access/refresh token oturumu
- Vardiya başlatma ve bitirme
- Atanan paketleri görüntüleme ve durum akışını ilerletme
- Uygun paketleri atomik olarak üstlenme
- SignalR ile anlık paket yenileme
- Yeni ve atanan paketler için push notification; uygulama açıksa sipariş listelerini otomatik yenileme
- Bildirime dokunulduğunda ilgili siparişe yönlenme ve siparişi listede vurgulama
- Push token değişimini çalışma anında yakalama, yeni token'ı kaydetme ve eski token'ı pasifleştirme
- Aktif vardiyada ön plan konum gönderimi
- Development build üzerinde arka plan konum takibi
- Bağlantı kesildiğinde son 100 konumu kurye hesabına bağlı, sıralı cihaz kuyruğunda saklama
- Uygulama yeniden ön plana geldiğinde bekleyen konumları otomatik gönderme ve kuyruk durumunu gösterme
- Oturum geçersizleştiğinde konum takibini ve yerel operasyon verilerini güvenli biçimde temizleme

## Yerelde çalıştırma

Node.js 22.13 veya üzeri gerekir. Fiziksel telefon ile bilgisayar aynı Wi-Fi ağında olmalıdır.

```bash
nvm use
npm install
cp .env.example .env.local
npm start
```

`.env.local` içindeki `YOUR_LAN_IP` değerini bilgisayarın yerel IP adresiyle değiştirin. Core ve Auth API'lerini fiziksel cihazın erişebilmesi için `0.0.0.0` üzerinde çalıştırın:

```bash
dotnet run --project backend/src/Services/Core/DeliveryOps.Core.Api --urls http://0.0.0.0:5100
dotnet run --project backend/src/Services/Auth/DeliveryOps.Auth.Api --urls http://0.0.0.0:5200
dotnet run --project backend/src/Services/Notifications/DeliveryOps.Notifications.Api --urls http://0.0.0.0:5300
```

Test hesabı: `courier@demo.local` / `CourierDemo123!`

Expo Go ile oturum, siparişler ve ön plan konumu denenebilir. Push token üretimi için `.env.local` dosyasına EAS projesindeki `EXPO_PUBLIC_EAS_PROJECT_ID` değeri eklenmelidir. Push notification ve arka plan konumu native yapılandırma gerektirdiği için development build kullanın:

```bash
npx eas build --profile development --platform android
# veya yerel iOS Simulator için
npx expo run:ios
```

Geliştirme build'inde Profil ekranındaki **Test paket bildirimi gönder** düğmesi, EAS/APNs bağlantısı olmadan yerel bildirim ve yönlendirme akışını sınamak içindir. Gerçek uzaktan push gönderimi için fiziksel cihaz, EAS project ID ve platform kimlik bilgileri gerekir. Çıkış yapılırken backend cihaz kaydı pasifleştirilir, cihazdaki token temizlenir ve native bildirim kaydı kaldırılır.

## Kontroller

```bash
npm run typecheck
npm run lint
npm run doctor
npx expo export --platform web
```

## iOS Simulator E2E

Core/Auth API'leri ve Metro çalışırken, development build kurulu ve bir iOS Simulator açık olmalıdır. Maestro testi temiz oturumla giriş yapar, vardiyayı başlatır, hazırladığı paketi üstlenir, teslim eder, vardiyayı kapatır ve çıkış yapar. Test geçici ayar/kredi değişikliklerini geri alır.

```bash
make mobile-e2e
# veya bu klasörde
npm run test:e2e:ios
```
