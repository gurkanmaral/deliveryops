# Mimari notları

## Servis sınırları

- **Core API:** işletme, şube, kurye, sipariş ve atama iş kuralları.
- **Auth API:** kullanıcı, rol, izin, cihaz ve token yaşam döngüsü.
- **Notifications API:** push, panel içi bildirim ve daha sonra SMS/e-posta.
- **Integrations API:** Yemeksepeti, Getir ve POS sağlayıcı adapter'ları.

Core içindeki bağımlılık yönü `Domain ← Queries ← Handlers` şeklindedir;
Infrastructure, Queries tarafından tanımlanan portları uygular ve API composition
root olarak bütün katmanları birleştirir. Bu kurallar unit test içindeki mimari
kontrollerle korunur. Auth daha küçük ve tek amaçlı olduğu için yapay proje
katmanlarına bölünmez; servis büyüdüğünde aynı sınırlar davranış bazında çıkarılır.

Production servisleri veritabanı migration'ını başlangıçta otomatik çalıştırmaz.
Migration'lar kontrollü deployment adımıdır; local Development profili gerektiğinde
`Database:AutoMigrate` ayarıyla kolaylık sağlar.

## Tenant güvenliği

Core modelindeki iş verileri `BusinessId` ile sınırlandırılır. Bu değer güvenilir JWT claim'inden üretilir; istemciden gelen tenant kimliği tek başına yetki kaynağı olarak kabul edilmez. Platform yöneticisi ve işletme kullanıcısı politikaları API seviyesinde ayrıdır.

Bu sınır uygulandı: `PlatformAdmin` bütün tenant kayıtlarını yönetebilir; işletme rollerinde `business_id` ve varsa `branch_id` JWT claim'leri sunucu tarafında zorlanır. Query string ile farklı tenant kimliği gönderilmesi erişim kapsamını genişletmez.

## Sipariş yaşam döngüsü

Durum geçişleri domain aggregate üzerinde doğrulanır ve her başarılı geçiş `order_status_history` tablosuna kullanıcı kimliğiyle yazılır. Siparişler fiziksel olarak silinmez; `DELETE` işlemi yalnızca geçerli aşamalarda iptal durumuna geçirir.

## Otomatik kurye atama

Atama politikası her işletme için ayrı saklanır. Otomatik onay/atama, kurye başına aktif paket sınırı, şube kuryesi önceliği, güncel konum zorunluluğu ve azami atama mesafesi panelden yönetilir.

Core API içindeki arka plan çalışanı, `WaitingForCourier` durumundaki siparişleri artan bekleme süresine göre işler. Aday kurye aktif, müsait, vardiyada ve sipariş şubesiyle uyumlu olmalıdır. Seçim sırası şube önceliği, mevcut paket yükü, şubeye mesafe ve konum güncelliğidir. Kurye bulunamazsa `order_dispatch_states` kaydı neden ve üstel geri çekilme ile sonraki deneme zamanını tutar.

Aynı işletmedeki paralel atamalar PostgreSQL transaction advisory lock ile sıralanır; sipariş ve kurye kayıtlarında optimistic concurrency ikinci koruma katmanıdır. Atama sonucunda sipariş durumu, kurye durumu, geçmiş ve notification outbox aynı transaction içinde kaydedilir.

Operasyon paneli kurye bekleyen siparişleri ayrı kuyruk endpoint'inden izler. Her sipariş için aynı sıralama motorundan üretilen uygun kurye önerileri ve `dispatch_attempts` tablosundaki otomatik, manuel, tekrar deneme ve kurye üstlenme geçmişi gösterilir. Operatör bekleyen siparişi anında yeniden kuyruğa alabilir; henüz teslim alınmamış bir siparişin kuryesini değiştirebilir. Yeniden atamada eski kurye gerçek zamanlı olarak bilgilendirilir ve yeni kurye bildirimi outbox üzerinden gönderilir.

## SLA ve operasyon alarmları

Her işletme kurye bekleme, teslim alma, teslimat ve kurye konum güncelliği için uyarı/kritik sürelerini ayrı tanımlar. Core API içindeki SLA worker bu eşikleri periyodik değerlendirir ve `operational_alerts` tablosunda problem başına tek bir alarm tutar. İşletme bazlı advisory lock, birden fazla API instance'ının aynı alarmı üretmesini engeller.

Alarm aynı sorun devam ederken çoğaltılmaz; süre kritik eşiğe ulaşınca seviyesi yükseltilir. Operatör alarmı “inceleniyor” olarak işaretleyebilir. Sipariş ilerlediğinde, iptal edildiğinde, teslim edildiğinde veya kurye yeniden konum gönderdiğinde alarm worker tarafından otomatik çözülür. Yeni, yükseltilmiş ve çözülmüş alarm durumları SignalR üzerinden işletme ve platform gruplarına yayınlanır.

## Raporlama ve dışa aktarma

Operasyon raporu seçilen tarih aralığında oluşturulan siparişleri kohort olarak değerlendirir. Gün sınırları `Europe/Istanbul` saat dilimine göre UTC'ye çevrilir; aralık en fazla 367 gün olabilir. İşletme kullanıcısının tenant kapsamı JWT claim'inden zorlanır, platform yöneticisi tek işletmeyi seçebilir veya tüm ağı birlikte inceleyebilir.

Atama, teslim alma, teslimat ve uçtan uca süreler `order_status_history` kayıtlarındaki ilk geçişlerden hesaplanır. Başarı oranının paydası yalnızca sonuçlanmış siparişlerdir (`Delivered`, `Cancelled`, `Returned`). API günlük hacim ile kurye, şube ve kaynak performansını tek veri sözleşmesinde sunar.

- `GET /api/v1/reports/operations`: tarih aralıklı JSON raporu
- `GET /api/v1/reports/operations/export?format=csv`: günlük metrik CSV'si
- `GET /api/v1/reports/operations/export?format=xlsx`: özet, günlük, kurye, şube ve kaynak çalışma sayfalarına sahip Excel dosyası

Export dosyaları istemcide hesaplanmaz; aynı rapor sorgusundan sunucu tarafında üretilir. Böylece panel ve indirilen dosya aynı tenant filtresi ile aynı metrik tanımını kullanır.

## Finansal mutabakat

Platform yöneticisi her işletme için teslim edilen paket başı ücret, teslim edilen sipariş tutarı üzerinden komisyon, iade paket işlem ücreti ve KDV oranı tanımlar. Varsayılan hizmet ücretleri sıfırdır; bu nedenle tarife açıkça girilmeden işletmeye borç üretilmez. İşletme kullanıcıları kendi tarifesini ve dönem dökümünü okuyabilir, ancak değiştiremez veya mutabakat kesinleştiremez.

Mutabakat dönemi siparişin oluşturulma tarihine değil `order_status_history` içindeki gerçek `Delivered`, `Returned` ve `Cancelled` geçiş zamanına göre, `Europe/Istanbul` gün sınırlarıyla hesaplanır. Taslak kaydedildiğinde hem operasyon adetleri hem de kullanılan tarife snapshot olarak saklanır. Tarife sonradan değişse bile kaydedilmiş dönem geçmişi değişmez; kesinleşmiş mutabakat tekrar hesaplanamaz. Çakışan tarih aralıklarına ikinci bir kayıt açılamaz.

- `GET/PUT /api/v1/billing/settings`: işletme fiyatlandırması
- `GET /api/v1/billing/preview`: canlı dönem hesabı
- `GET/POST /api/v1/billing/settlements`: mutabakat listesi ve taslak oluşturma
- `POST /api/v1/billing/settlements/{id}/finalize`: değişmez dönem snapshot'ını kesinleştirme
- `GET /api/v1/billing/settlements/{id}/document?format=pdf|xlsx`: yalnızca kesinleşmiş mutabakatın PDF veya Excel belgesini indirme

Kesinleşme sırasında `MUT-yyyyMM-XXXXXXXX` biçiminde benzersiz bir belge numarası atanır. PDF ve Excel, canlı sipariş veya güncel tarife tablolarından tekrar hesaplanmaz; mutabakat kaydındaki işletme, dönem, tarife, paket adetleri ve tutar snapshot'ını kullanır. Bu nedenle tarife daha sonra değişse de daha önce kesinleşmiş bir belgenin içeriği değişmez. Kurumsal logo ve vergi bilgileri belge şablonuna sonraki aşamada eklenebilir.

## İşletme kredileri

Her işletmenin tek bir kredi hesabı ve değişmez bir hareket defteri vardır. Yeni işletme sıfır bakiye ile açılır. Platform yöneticisi kredi yükleyebilir; işletme kullanıcıları kendi bakiyesini ve hareketlerini okuyabilir.

Her başarılı yeni sipariş tam olarak `1` kredi tüketir. Kredi düşümü, sipariş kaydı ve hareket defteri kaydı aynı veritabanı işlemi içinde tamamlanır. Bakiye yetersizse sipariş oluşturulmaz; tekrar gönderilen aynı dış sipariş de ikinci kez kredi tüketmez. İptal veya iade otomatik kredi iadesi oluşturmaz; gerektiğinde ayrı bir düzeltme/iade hareketiyle ele alınacaktır.

Kredi yüklemeleri `1.000`, `5.000` ve `10.000` kredilik tanımlı paketlerden yapılır. Manuel düzeltmede pozitif veya negatif miktar ve açıklama zorunludur; bakiye sıfırın altına indirilemez. Sipariş kredisi iadesi özgün tüketim hareketine bağlanır ve aynı sipariş yalnızca bir kez iade edilebilir. Bütün yönetim işlemleri `Idempotency-Key` başlığı ve veritabanı benzersizlik kısıtıyla çift çalıştırmaya karşı korunur. Hesap ve hareket değişiklikleri mevcut audit interceptor tarafından kullanıcı ve işletme bilgisiyle kaydedilir.

Her işletmenin varsayılan düşük bakiye eşiği `100` kredidir; `0` değeri uyarıyı kapatır. Bakiye eşik veya altına indiğinde operasyon bildirim merkezinde uyarı, sıfırlandığında kritik alarm oluşur. Kredi yeniden eşik üstüne çıktığında alarm otomatik çözülür ve SignalR üzerinden panele yansıtılır.

- `GET /api/v1/credits/account`: işletmenin güncel ve yaşam boyu kredi özeti
- `GET /api/v1/credits/packages`: tanımlı kredi paketleri
- `GET /api/v1/credits/transactions`: kredi hareketleri
- `POST /api/v1/credits/top-up`: paket yükleme
- `POST /api/v1/credits/adjustments`: açıklamalı manuel düzeltme
- `POST /api/v1/credits/refunds`: sipariş tüketimini bir kez iade etme
- `PUT /api/v1/credits/settings`: düşük bakiye eşiği

## Veri sahipliği

Her servis kendi şemasının ve verisinin sahibidir. Servisler arası durum değişiklikleri RabbitMQ üzerinden idempotent entegrasyon olaylarıyla aktarılacaktır. Dashboard sorguları başlangıçta Core veritabanından, ölçek ihtiyacında ayrı bir okuma modelinden beslenecektir.
