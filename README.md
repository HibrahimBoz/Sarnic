# Video Transkriptör

Kapalı ağda (internetsiz) Türkçe ağırlıklı ders videoları için iki ürün:

1. **Video Transkriptör** (`kaynak/app/videohat`, Python): videodan transkript (SRT/VTT/TXT/JSON) çıkarır, sesi ve görüntüyü iyileştirir. Motor: faster-whisper (large-v3). Elektronik harp terim sözlüğü (`kaynak/terimler.txt`) ve düzeltme katmanı içerir. Çok kameralı ve parçalı kayıtlar için `isle-grup` komutu ve ses tabanlı senkron (`senkron.py`, `parca_zinciri.py`, `ders_json.py`) vardır.
2. **Video Portalı** (`kaynak/portal`, .NET 8 + WPF + LibVLCSharp): işlenmiş dersleri arar, transkriptteki cümleye tıklayınca videoyu o dakikada açar, çok açılı dersleri senkron oynatır. Düzenleme modu parola ister; her düzeltme ayrı dosyadır ve kim yazdığı kaydedilir.

Yeni çıktı klasörleri `<ad>_islenmis` ekiyle oluşturulur. Portal eski `<ad>_videoperisi` klasörlerini de tarar.

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
Profil: `kaynak/portal/Portal.Uygulama/Properties/PublishProfiles/ClickOnce.pubxml` (self-contained, win-x64). Çıktı: `bin\ClickOnce\` (~163 MB, 471 dosya). Yayından önce `kaynak/portal/Portal.Uygulama/ayar.json` içindeki `kok` alanını sunucu kökü (`\sunucu\Dersler`) ile doldurun; kullanıcı kısayoluna argüman verilemez. Bildirimler (manifest) imzalanmalıdır: kendi sertifikanızı (`-p:SignManifests=true -p:ManifestCertificateThumbprint=...`) kullanın ve BT'ye "Güvenilen Yayıncılar" deposuna dağıttırın. İmzasız kurulum denenmedi.
