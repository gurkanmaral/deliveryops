# DeliveryOps POS Bridge

Windows işletme bilgisayarında çalışan POS sipariş köprüsü. POS yazılımının ürettiği normalize sipariş JSON dosyalarını izler, DeliveryOps Integrations API'ye yollar ve sonucu yerel olarak arşivler.

## Projeler

- `DeliveryOps.PosBridge.Core`: işletim sisteminden bağımsız kuyruk, doğrulama ve webhook gönderim mantığı.
- `DeliveryOps.PosBridge.Wpf`: Windows masaüstü arayüzü ve DPAPI tabanlı secret saklama.
- `DeliveryOps.PosBridge.Cli`: aynı hattı Windows dışındaki geliştirme makinelerinde çalıştırmak için CLI.
- `DeliveryOps.PosBridge.Core.Tests`: kuyruk ve hata davranışlarının birim testleri.

## Windows'ta çalıştırma

1. Admin panelde `Entegrasyonlar` sayfasından `POS` bağlantısı oluşturun.
2. Gösterilen webhook URL ve `X-DeliveryOps-Key` değerini kopyalayın.
3. `DeliveryOps.PosBridge.sln` dosyasını Rider veya Visual Studio ile açın.
4. Başlangıç projesi olarak `DeliveryOps.PosBridge.Wpf` seçin ve çalıştırın.
5. Webhook URL, secret ve POS sipariş klasörünü girip ayarları kaydedin.
6. `Bridge'i başlat` düğmesine basın.

Secret düz metin olarak diske yazılmaz; Windows DPAPI `CurrentUser` kapsamıyla şifrelenir. Ayar dosyası `%LOCALAPPDATA%\DeliveryOps\PosBridge\settings.json` konumundadır.

## POS dosya sözleşmesi

POS entegrasyonu izlenen klasöre şu yapıda UTF-8 `.json` dosyası bırakmalıdır:

```json
{
  "externalOrderId": "POS-2026-0001",
  "customerName": "Test Müşteri",
  "customerPhone": "05550000000",
  "deliveryAddress": "Kadıköy, İstanbul",
  "totalAmount": 245.50
}
```

POS yazılımının önce `.tmp` dosyasına yazıp işlem tamamlandığında uzantıyı `.json` olarak değiştirmesi önerilir. Bridge, hâlâ kilitli olan dosyaları sonraki çevrime bırakır.

- Başarılı dosyalar `processed` klasörüne taşınır.
- Geçersiz veya kalıcı olarak reddedilen dosyalar `failed` klasörüne taşınır ve yanına `.error.txt` yazılır.
- Müşteri verisinin gereksiz yere diskte kalmaması için `processed` arşivi 7 gün, `failed` arşivi 30 gün sonra otomatik silinir.
- Ağ hatası, `429` veya `5xx` cevabında dosya yerinde kalır ve yeniden denenir.
- Integrations API aynı dış sipariş numarasını idempotent işlediği için yeniden gönderim ikinci sipariş oluşturmaz.

## CLI

Secret'ı komut geçmişine yazmamak için environment variable kullanın:

```bash
export DELIVERYOPS_POS_WEBHOOK_SECRET="panelden-alinan-secret"
dotnet run --project src/DeliveryOps.PosBridge.Cli -- \
  --webhook "http://localhost:5400/api/v1/webhooks/CONNECTION_ID/orders" \
  --inbox "./inbox" \
  --once
```

## Build ve test

```bash
dotnet test DeliveryOps.PosBridge.sln
dotnet build src/DeliveryOps.PosBridge.Wpf/DeliveryOps.PosBridge.Wpf.csproj
dotnet publish src/DeliveryOps.PosBridge.Wpf/DeliveryOps.PosBridge.Wpf.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -o artifacts/win-x64
```

NarPOS'a özel doğrudan veritabanı, SDK veya local API adaptörü bu çekirdeğin önüne eklenecektir. Bunun için NarPOS sürümü, resmi entegrasyon dokümanı ve test ortamı bilgileri gerekir; mevcut klasör adaptörü sağlayıcıdan bağımsız güvenli başlangıç yoludur.
