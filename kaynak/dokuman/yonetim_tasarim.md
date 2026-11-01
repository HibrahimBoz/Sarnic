# Sarnıç'tan Atölye yönetimi ve ajan — tasarım taslağı (2026-11-01)

Durum: onaylandı (2026-11-01).

## Amaç
- Kullanıcı Atölye'yi Sarnıç'tan yönetir: iş başlatma, izleme, iptal, kuyruk temizleme.
- Kullanıcı Sarnıç'tan ajana soru sorar. Ajan dizinde arar ve kaynaklı cevap yazar.
- 100 kullanıcı aynı anda bağlansa da sistem düşmez. Normal arama ajandan etkilenmez.

## Kararlar
| Konu | Karar | Kaynak |
|---|---|---|
| Atölye bilgisayarı | Sürekli açık, RTX 5060, 32 GB RAM | Kullanıcı |
| Yönetim yapısı | A: ağ servisi. Yönetici izni yoksa B: paylaşım klasörü | Kullanıcı |
| Parola | İlk kuyruk temizlemede bir kez sorulur. Sarnıç kapanana kadar yönetici kalır | Kullanıcı |
| Roller | Şimdilik herkes yönetici. Sonra Windows kullanıcı adına göre yetki | Kullanıcı |
| Hangi parola | Yönetim için yeni parola | Kullanıcı |

## Bileşenler
- **Atölye Servisi**: Atölye bilgisayarında çalışan ayrı süreç. İş kuyruğunu, GPU sırasını ve ajanı yönetir.
- **İş Kuyruğu**: `is\kuyruk.db` (SQLite). İş yalnız `\\sunucu\...` yolunu kaydeder, dosya kopyalamaz.
- **Bağlantı katmanı**: Sarnıç'ta tek arayüz, iki taşıyıcı. Sarnıç önce A'yı dener, olmazsa B'ye geçer.
- **Yönetim ekranı**: Sarnıç'ta yeni pencere.
- İşleme kodu ağ kilidiyle ayrı süreçte kalır. Ağa yalnız servis açılır.

## Taşıyıcı A: ağ servisi
- HTTPS, kendinden imzalı sertifika, yalnız kapalı ağ. Sarnıç sertifika parmak izini ayardan denetler. Düz HTTP kullanılmaz: NTLM aktarma ve oturum çalma riski (Argon).
- Canlı durum olay akışıyla gelir.
- Etki alanı (domain) varsa kimlik Windows kimlik doğrulamasıyla sunucuda alınır.
- Etki alanı yoksa NTLM aynı yerel hesabı ister. Bu durumda kullanıcı adı yalnız bildirimdir, yetki kanıtı sayılmaz.
- Gereken izin: bir kez güvenlik duvarında tek port.

## Taşıyıcı B: yönetici izni yoksa
Python'un port açması yönetici istemez. Asıl engel gelen bağlantıyı kapatan güvenlik duvarıdır. Bu nedenle B ağ portu kullanmaz.

- Paylaşım: `\\sunucu\Sarnic-Veri\yonetim\`.
  - `durum.json`: Servis yazar. Kuyruk, ilerleme, GPU.
  - `komut\<guid>.json`: Sarnıç yazar. Servis okur, işler, sonra `sonuc\<guid>.json` yazar.
  - `soru\`, `cevap\`: ajan için aynı düzen. Sorular servis tarafında `kuyruk.db` içine alınır, çökmede kaybolmaz.
- Her yazma atomik olur: önce `gecici\<guid>.tmp`, sonra yeniden adlandırma. Var olan dosyanın üzerine yazma reddedilir.
- Yoklama aralığı 2 sn (tahmin, ölçülecek). SMB önbelleği görünümü geciktirebilir.
- Dosya sahibi yalnız denetim kaydına ipucu olarak yazılır, yetki kanıtı sayılmaz. Domain yoksa ya da ortak hesapta sahip bilgisi yanıltıcıdır (Argon).
- Parola dosyaya yazılmaz. Sarnıç `guid + zaman + komut` için HMAC üretir. Servis 5 dakikalık pencere ve görülen guid listesiyle tekrar oynatmayı reddeder.
- Parola en az 12 karakter olur. Paylaşımı okuyan kişi HMAC üzerinden çevrimdışı parola denemesi yapabilir.
- Kalp atışı dosyası yok: 100 istemci paylaşımı boğar. Yönetici durumu Sarnıç belleğinde kalır, her yetkili komut imzalanır.
- Eksikler ve risk:
  - Canlı akış yok, gecikme var.
  - Klasör izinleri ayarlanamazsa her kullanıcı başkasının sorusunu okur, siler ya da sahte cevap bırakır. B bu nedenle yalnız güvenilir ekip için kabul edilir.
  - İzinler ayarlanabilirse `durum.json`, `sonuc\`, `cevap\` için yalnız servis hesabı yazar.
- Paylaşım da yoksa son çare: yönetim ve ajan kapanır, Sarnıç yalnız arama yapar.

## Yönetici izni olmadan servisi çalıştırma
- Windows hizmeti kurulamaz. Süreç kullanıcı oturumunda çalışır.
- Kullanıcı düzeyi Görev Zamanlayıcı görevi oturum açılınca başlatır, süreç düşünce yeniden başlatır.
- Sınır: bilgisayar yeniden başlarsa biri oturum açana kadar servis kapalıdır. Oturum kapatılırsa servis durur.
- Yönetici izni varsa Windows hizmeti kurulur ve bu sınır kalkar.

## Yetki
- Her işlemi serviste denetleyin. Sarnıç içindeki denetim güvenlik sayılmaz.
- A'da parola doğrulanınca servis oturum belirteci verir. Belirteç yalnız bellekte durur. Sarnıç kapanınca ya da 30 sn kalp atışı gelmezse biter (ölçülecek).
- Parola yalnız kuyruk temizlemede sorulur. Yönetici durumu kapanana kadar yeniden sorulmaz.
- "Herkes yönetici" döneminde başlatma ve iptal parolasızdır. Rol listesi gelince bu işlemler de yetkiye bağlanır.
- Hatalı parolada küresel kilit yok: kilit herkesi dışarıda bırakır. Her denetim 2 sn sabit gecikme alır.
- Tüm yönetim işlemleri `is\denetim.log` dosyasına yazılır: kullanıcı, zaman, işlem, sonuç.
- Temizleme özgün dosyayı ve çıktıyı silmez. Yalnız bekleyen kayıtları kaldırır. Çalışan iş ayrı olarak iptal edilir.

## GPU sırası
- RTX 5060, 8 GB (kullanıcı). Kullanıcı kararı: tüm modeller ayakta durmaz. İş kuyruğa girer, yalnız işlem gören model ayakta olur, işi biten çıkar ve kapanır.
- Uygulama: tek işçi, her iş ayrı süreç. Süreç bitince model bellekten çıkar.
- Dizinde kısıtlı klasör olursa ortak önbellek içerik sızdırır. Roller gelince önbellek kullanıcı yetkisine bağlanır.

## 100 kullanıcı (tahmin, yük testiyle ölçülecek)
- Kişi başına 1 çalışan, 2 bekleyen soru. Kuyrukta en çok 200 soru.
- Tek GPU'da toplam hız saniyede 40-80 token. 1500 tokenlik 100 soru 30-60 dakika sürer.
- Kullanıcı sırasını görür ve iptal eder. Aynı soru ve aynı dizin sürümü önbellekten döner.
- Yük testi 10, 50 ve 100 kullanıcıyla yapılır. Süre kabul edilmezse GPU eklenir.

## Yapım sırası
1. İş Kuyruğu ve servis, yalnız yerel.
2. Taşıyıcı A ve B, aynı komut kümesi.
3. Yönetim ekranı, parola, denetim kaydı.
4. Ajan: yerel model, araçlar, kaynaklı cevap.
5. Yük testi.

## Ortam
- Windows etki alanı var (kullanıcı). Port izni bilinmiyor.

## Ajan: yapılan ve ölçülen (2026-11-01)
- Çalıştırıcı: llama.cpp b12029 Vulkan. Model: Qwen3-8B Q4_K_M (5,03 GB, Apache-2.0). Yalnız 127.0.0.1 üzerinde açılır.
- Ajan yalnız okur: `belge_ara`, `dosya_ara`, `sayfa_oku`. Dosya yazmaz, komut çalıştırmaz.
- Ön arama ve istenen sayfa kodla yapılır. Kaynak numarası servisçe denetlenir, uydurma numara silinir.
- Video transkripti ajanın kapsamında değil: servisin video kök klasörü yok.
- Ölçüm (RTX 3070 Laptop 8 GB, deneme verisi): 58-60 belirteç/sn, model açılışı yaklaşık 20 sn, GPU belleği 7155 MiB.
- Yük (45 sn, ağ kanalı, tek Windows hesabı): 100 istemcide 0 hata, durum p95 2 ms, komut p95 5 ms.
- Kuyruk: 100 ayrı kişinin sorusu 177 sn'de bitti (100 bitti, 0 hata). Belgeler kısa olduğu için cevap süresi gerçek arşivde daha uzun olabilir (ölçülmedi).
- Kabul edilen risk: paylaşım kanalında dosya sahibi okunamazsa kimlik bildirimdir. Bu kanal güvenilir ekip içindir.
