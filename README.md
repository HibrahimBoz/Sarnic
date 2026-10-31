# Sarnıç Atölye ve Sarnıç

Kapalı ağda çalışan birleşik arama paketi üç temel yetenek sunar:

1. **Dosya adı araması**: Tanımlı klasörlerdeki dosya ve klasör adlarında anında alt dize araması yapar.
2. **Belge içeriği ve OCR**: PDF, Office ve görüntü dosyalarının metinlerini ve taranmış sayfalarını arar.
3. **Video transkript araması**: Ders videolarının konuşma dökümlerinde ve teknik terimlerinde arama yapar.

Paket iki parçadan oluşur:

1. **Sarnıç Atölye** (`kaynak/app/videohat`, Python): Videolardan transkript çıkarır, belgelerden metin çıkarıp OCR uygular, eski belge biçimlerini dönüştürür ve arama dizini üretir. Elektronik harp terim sözlüğü (`kaynak/terimler.txt`) ve düzeltme katmanı içerir. Çok kameralı ve parçalı kayıtlar için `isle-grup` komutu ve ses tabanlı senkron (`senkron.py`, `parca_zinciri.py`, `ders_json.py`) vardır.
2. **Sarnıç** (`kaynak/portal`, .NET 8 + WPF + LibVLCSharp): Dosya adlarını, belge sayfalarını ve video transkriptlerini tek noktadan arar. Transkriptteki cümleye tıklayınca videoyu o dakikada açar, belge sonucuna tıklayınca ilgili sayfayı açar, çok açılı dersleri senkron oynatır. Sonuçları doğrudan ArtCraft taşınabilir uygulamalarıyla açıp düzenleme olanağı sunar. Düzenleme modu parola ister; her düzeltme ayrı dosyadır ve kimin yazdığı kaydedilir.

Yeni çıktı klasörleri `<ad>_islenmis` ekiyle oluşturulur. Sarnıç eski `<ad>_videoperisi` klasörlerini de tarar.
Portal paketindeki `ayar.json` dosyasının `kurum` alanı pencere başlığına ön ek olur: `<kurum> Sarnıç`.

- **Eski belge biçimleri (2000 ve öncesi)**: LibreOffice, DjVuLibre ve 7-Zip entegrasyonuyla eski metin, hesap tablosu, sunum ve arşiv biçimleri modern biçimlere dönüştürülür ve düzenlenebilir kopyaları (`duzenlenebilir.docx` vb.) depoda saklanır.
- **ArtCraft entegrasyonu**: WordCraft, GridCraft, DeckCraft, PdfCraft ve PhotoCraft taşınabilir uygulamaları pakette yerleşik gelir (`artcraft\` klasörü). Sonuçlara sağ tıklayarak veya belge görüntüleyiciden doğrudan düzenleme yapılır; düzenlenebilir kopyalar `%USERPROFILE%\Documents\Sarnic\` altına kopyalanır ve arşivdeki özgün dosya asla değişmez. Sistemde birden çok sürüm varsa en güncel sürüm kendiliğinden seçilir.

## Belge ve dizin

Komutlar `videohat` paketi üzerinden çalıştırılır:
```
python -m videohat dizinle --kok "\\sunucu\Dersler"
python -m videohat belge "D:\belgeler\rapor.pdf" --profil uyarlamali
```

Tasarım sözleşmesi ve ortak şema dosyaları:
- Tasarım sözleşmesi: `kaynak/dokuman/bulgu_spec.md`
- Ortak veritabanı şeması: `kaynak/ortak/dizin_sema.sql`
- Normalizasyon test vakaları: `kaynak/ortak/normalizasyon_vakalari.json` ve `kaynak/ortak/dosya_normalizasyon_vakalari.json`

## Derleme ve test
```
dotnet build kaynak/portal/Portal.sln
dotnet test  kaynak/portal/Portal.sln
python -m unittest discover -s kaynak/tests -p "test_*.py"   # PYTHONPATH=kaynak/app
```
Portalı yayınlamak: `dotnet publish kaynak/portal/Portal.Uygulama/Portal.Uygulama.csproj -c Release -r win-x64 --self-contained`.

## Bilinen sınırlar (ölçüldü)
- libvlc, `\\sunucu\paylaşım\...` yazımını açamaz; portal bunu `//sunucu/paylaşım/...` biçimine çevirir (`VlcYolu`).
- Ağ paylaşımındaki `.exe` Windows "Dosya Aç - Güvenlik Uyarısı" gösterir; portalı yerel diskten çalıştırın veya sunucuyu "Yerel intranet" bölgesine alın.
- Çok açılı oynatıcı ve düzenleme arayüzü gerçek derste ve Windows 10'da denenmedi.

Üçüncü taraf bileşenler (ffmpeg, libvlc, faster-whisper, modeller) bu depoda yoktur; kendi lisanslarına tabidir.

## ClickOnce yayını
`dotnet publish` ClickOnce'ı tamamlayamaz (MSB4803: UpdateManifest görevi .NET Core MSBuild'de yok; ölçüldü). Visual Studio'nun MSBuild'i ile yayınlayın:
```
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" kaynak\portal\Portal.Uygulama\Portal.Uygulama.csproj -restore -t:Publish -p:PublishProfile=ClickOnce -p:ApplicationVersion=1.0.0.1
```
Profil: `kaynak/portal/Portal.Uygulama/Properties/PublishProfiles/ClickOnce.pubxml` (self-contained, win-x64). Çıktı: `bin\ClickOnce\` (~163 MB, 471 dosya). Yayından önce `kaynak/portal/Portal.Uygulama/ayar.json` içindeki `kok` alanını sunucu kökü (`\\sunucu\Dersler`) ile doldurun; kullanıcı kısayoluna argüman verilemez. Bildirimler (manifest) imzalanmalıdır: kendi sertifikanızı (`-p:SignManifests=true -p:ManifestCertificateThumbprint=...`) kullanın ve BT'ye "Güvenilen Yayıncılar" deposuna dağıttırın. İmzasız kurulum denenmedi.
