# Bulgu — birleşik arama paketi: tasarım sözleşmesi (sürüm 1)

Bu belge yazarların uyacağı sözleşmedir. Ortak dosyalar: `kaynak/ortak/dizin_sema.sql` (veritabanı şeması,
TEK KAYNAK) ve `kaynak/ortak/normalizasyon_vakalari.json` (Python ve C# normalizasyonunun aynı sonucu
verdiğini kanıtlayan vakalar). Bu iki dosya DEĞİŞTİRİLMEZ; gerekirse Claude değiştirir.

## 1. Amaç

Kapalı ağda (internetsiz) tek paket, üç yetenek, tek arama kutusu:

1. **Dosya adı araması** (Everything benzeri): tanımlı kök klasörlerdeki her dosya ve klasör adında, yazarken
   anında alt dize araması; uzantı, boyut, tarih, yol süzgeçleri; sütuna göre sıralama.
2. **Belge içeriği araması**: PDF, Office, OpenDocument, RTF, metin, HTML, e-posta, görüntü. Taranmış sayfalar
   OCR ile okunur. Sonuç sayfa düzeyindedir; tıklayınca belge o sayfada açılır.
3. **Video transkript araması**: mevcut davranış aynen kalır (`MetinAramaMotoru`, düzeltme katmanı).

Belgeler derse bağlı olabilir (ders klasöründe) ya da bağımsız olabilir. Ölçek: yüz binlerce sayfa, tek belgede
1000+ sayfa.

**Ad:** paketin ürün adı **Bulgu**. Parçalar: *Bulgu Portalı* (WPF arayüz) ve *Bulgu İşleyici* (Python:
video transkript + belge/OCR + dizinleyici). İç kod adları (`videohat` paketi, `Portal.*` ad alanları,
`_islenmis` eki) DEĞİŞMEZ: testler, eski kurulumlar ve klasör düzeni korunur.

## 2. Mimari

```
kök klasör(ler) ──► Bulgu İşleyici `dizinle` (tek yazar)
                     ├─ 1. tarama: dosya/klasör kataloğu (ad, yol, boyut, tarih)  → dizin.db
                     ├─ 2. içerik: metin çıkarma + OCR + aranabilir PDF          → depo/belgeler/<sha>/
                     └─ 3. yayın: VACUUM INTO depo/yayin/dizin-<zaman>.db + guncel.txt
                                             │
Bulgu Portalı (çok okur) ── guncel.txt'yi izler, anlık görüntüyü yerel diske kopyalar, salt okur
                         ├─ Dosyalar sekmesi   (dosya + dosya_fts)
                         ├─ Belgeler sekmesi   (sayfa + sayfa_fts)
                         ├─ Videolar sekmesi   (mevcut MetinAramaMotoru)
                         └─ Tümü               (üçünün birleşimi)
```

Neden tek yazar + anlık görüntü: SQLite ağ paylaşımında eşzamanlı yazmaya güvenilmez. Yazar yerel diskte çalışır,
okur kendi yerel kopyasını açar. Okurlar birbirini ve yazarı kilitlemez.

## 3. Ayarlar

### 3.1 `ayar.toml` — yeni `[dizin]` ve `[ocr]` bölümleri (Python, `ayar.py`'ye dataclass olarak eklenir)

```toml
[dizin]
kokler = []                 # taranacak kökler; boşsa komut satırındaki --kok zorunlu
depo = ""                   # dizin deposu; boşsa <ilk kök>\_bulgu
haric = ["_bulgu", "$RECYCLE.BIN", "System Volume Information", "_ara", ".git", "node_modules"]
calisma_db = ""             # yazarın yerel veritabanı; boşsa <paket kökü>\is\dizin\dizin.db
yayin_dk = 10               # en geç bu kadar dakikada bir yayın
tarama_dk = 15              # izleme kipinde iki tarama arası
max_dosya_mb = 2048         # bundan büyük dosyanın içeriği işlenmez (adı yine dizinlenir)
isci = 0                    # OCR süreç sayısı; 0 = max(1, çekirdek/2)
sakla_yayin = 3             # tutulacak eski anlık görüntü sayısı

[ocr]
profil = "uyarlamali"       # "hizli" | "uyarlamali" | "kalite"
dpi = 300                   # PDF sayfasını OCR için işleme çözünürlüğü
min_metin = 40              # sayfada bundan az gömülü metin karakteri varsa sayfa OCR'lanır
ikinci_esik = 0.80          # bu güvenin altındaki satır ikinci motorla yeniden okunur
cihaz = "auto"              # "auto" | "cpu" | "cuda" (onnxruntime CUDAExecutionProvider varsa)
sayfa_zaman_asimi_sn = 180
gomulu_gorsel = true        # DOCX/PPTX içindeki büyük görselleri OCR'la (>= 200x200 piksel)
tesseract = true            # araclar\tesseract\tesseract.exe varsa ikinci görüş olarak kullan
sozluk = "sozluk\\tr_kelime.txt"   # Türkçe sözcük sıklık listesi (yoksa sözlük düzeltmesi yalnız terimler.txt ile)
```

Geçersiz değer → `AyarHatasi` (mevcut desen: `__post_init__`).

### 3.2 Portal `ayar.json`

```json
{ "kok": "", "dizin": "" }
```
`dizin` boşsa `<kok>\_bulgu`. Portal yayın klasörünü `<dizin>\yayin` olarak okur.

## 4. Normalizasyon (iki dilde aynı)

`TrNormalizasyon.Normalle` (C#, mevcut) referanstır. Python karşılığı `videohat/dizin/normal.py: normalle(s)`:
- `ı İ I i î Î → i`, `ş Ş → s`, `ğ Ğ → g`, `ü Ü û Û → u`, `ö Ö → o`, `ç Ç → c`, `â Â → a`;
- `' ’ ‘ \` ´` → boşluk; A-Z → küçük harf;
- Unicode kategorisi P* (noktalama), S* (simge), Z* ya da `\t\n\v\f\r\x85\xa0` (boşluk), Cc (denetim) → boşluk;
- diğer karakter → `c.lower()` (sonuç tek karakter değilse `c` kalır);
- ardışık boşluk teke iner, baş ve son boşluk atılır.
`normalizasyon_vakalari.json` her iki test takımında koşulur.

## 5. Bulgu İşleyici (Python) — yeni modüller

Paket: `kaynak/app/videohat/`. Yeni alt paketler `belge/` ve `dizin/`. Mevcut kod üslubu: Türkçe ad, kısa yorum,
`logging.getLogger("videohat")`, hatalar `hatalar.py`'deki sınıflarla (`IsHatasi` vb.).

### 5.1 Bağımlılıklar (paketleyici `hazirla.ps1` kurar)
`pymupdf`, `rapidocr` (onnxruntime ile; onnxruntime zaten var), `opencv-python-headless`, `numpy`, `pillow`,
`python-docx`, `python-pptx`, `openpyxl`, `olefile`. İsteğe bağlı: `extract-msg` (MSG e-posta). Eski ikili Office
(`.doc .xls .ppt`) için `araclar\libreoffice\program\soffice.exe` varsa `--headless --convert-to` ile çevrilir;
yoksa `durum='desteklenmiyor'`, `hata='Eski Office biçimi: LibreOffice bulunamadı'`.

**OCR modelleri çevrimdışı:** RapidOCR modelleri internetten indirmeye ÇALIŞMAMALI. Modeller
`<paket kökü>\modeller-ocr\` altındadır ve motora açık yolla verilir (`Det.model_path`, `Rec.model_path`,
`Cls.model_path`, gerekirse `Rec.rec_keys_path`). Dosyalar: `PP-OCRv6_det_small.onnx`, `PP-OCRv6_det_medium.onnx`,
`PP-OCRv6_rec_small.onnx`, `PP-OCRv6_rec_medium.onnx`, `ch_ppocr_mobile_v2.0_cls_mobile.onnx`.
`yollar.py`'ye `ocr_model(ad) -> Path` (yoksa `OrtamHatasi`) ve `tesseract() -> Path | None` eklenir.

### 5.2 Veri modeli (`belge/model.py`)
```python
@dataclass
class OcrSatir: metin: str; guven: float; kutu: tuple[float, float, float, float]  # x0,y0,x1,y1 görüntü pikseli
@dataclass
class Sayfa:
    no: int; etiket: str; kaynak: str            # 'metin' | 'ocr'
    metin: str; guven: float | None = None
    satirlar: list[OcrSatir] = field(default_factory=list)   # yalnız OCR'da; aranabilir PDF için
    gorsel_boyut: tuple[int, int] | None = None              # OCR görüntüsünün (genişlik, yükseklik) pikseli
@dataclass
class BelgeSonucu:
    bicim: str; sayfalar: list[Sayfa]; uyarilar: list[str]; aranabilir_pdf: Path | None
```

### 5.3 Metin çıkarma (`belge/cikar.py`)
`def cikar(yol: Path, ayar: Ayar, ocr: OcrMotoru | None, ara: Path) -> BelgeSonucu` — uzantıya göre dağıtır:

| Biçim | Sayfa birimi | Yöntem |
|---|---|---|
| `.pdf` | PDF sayfası (`Sayfa n`) | PyMuPDF `get_text("text", sort=True)`. Gömülü metin `< ocr.min_metin` karakter ise ya da sayfanın ≥ %60'ı görüntüyse ve metni azsa → OCR. Parolalı PDF → `IsHatasi("Parolalı PDF")`. |
| `.docx .docm` | Sayfa sonu (`w:br w:type="page"`, `w:lastRenderedPageBreak`) ile bölünür; yoksa paragraf sınırında ~3000 karakterlik `Bölüm n` | python-docx; tablolar satır satır (hücreler sekmeyle), üst/alt bilgi ilk bölüme. Gömülü görseller (≥200×200) OCR'lanıp o bölüme eklenir (`ocr.gomulu_gorsel`). |
| `.pptx .pptm` | Slayt (`Slayt n`) | python-pptx; şekil metni + tablo + konuşmacı notu (`Not:` öneki); gömülü görsel OCR. |
| `.xlsx .xlsm` | Sayfa başına 200 satırlık parça (`<SayfaAdı> (satır a-b)`) | openpyxl `read_only=True, data_only=True`; boş satır atlanır; hücreler sekmeyle. |
| `.odt .ods .odp` | odt: `Bölüm n` (~3000 kr), ods: tablo parçası, odp: slayt | zip + `content.xml` stdlib `xml.etree`. |
| `.rtf` | `Bölüm n` | kendi RTF soyucu (kontrol sözcükleri, `\'hh` cp1254 kaçışları, `\uN` Unicode, gruplar `{\*...}` atlanır). |
| `.txt .csv .tsv .md .log .xml .json .ini .cfg .srt .vtt` | `Bölüm n` (~3000 kr) | kodlama: BOM (utf-8/utf-16) → utf-8 dene → cp1254. |
| `.html .htm` | `Bölüm n` | `html.parser`; `script/style` atlanır. |
| `.eml` | Gövde `İleti`; ekler `Ek: <ad> / <alt etiket>` | `email` stdlib; ekler geçici dosyaya yazılıp aynı `cikar` ile (derinlik 1) işlenir. |
| `.msg` | `.eml` gibi | `extract_msg` varsa; yoksa `desteklenmiyor`. |
| `.doc .xls .ppt` | çevrilen biçimin birimi | LibreOffice ile `ara` klasörüne çevrilir. |
| `.png .jpg .jpeg .tif .tiff .bmp .gif .webp` | Görüntü/kare (`Sayfa n`; çok sayfalı TIFF) | OCR. |

Sayfa metni `strip` edilir; tamamen boş sayfa YİNE kaydedilir (sayfa numaraları kaymasın), metni `''`.

### 5.4 OCR algoritması (`belge/ocr.py`, `belge/onisleme.py`, `belge/sozluk.py`)

`class OcrMotoru` (süreç başına bir örnek; tembel model yükleme):
`def oku(gorsel: np.ndarray) -> tuple[list[OcrSatir], float]` (satırlar + sayfa ortalama güveni).

Adımlar:
1. **Ön işleme** (`onisleme.py`): gri ton; kısa kenar < 1000 piksel ise 2× büyütme (INTER_CUBIC);
   eğiklik kestirimi (Otsu ikilileştirme → metin piksellerinde `minAreaRect` ya da izdüşüm profili, ±15°);
   |açı| ≥ 0,3° ise döndür (beyaz kenar). Düşük karşıtlıkta (gri std < 40) CLAHE. Gürültü kestirimi yüksekse
   (Laplacian varyansı eşiği) hafif `fastNlMeansDenoising`. Her dönüşüm `Donusum` kaydına yazılır
   (ölçek, açı, yön) — kutular özgün sayfa koordinatına geri çevrilebilsin.
2. **Yön**: 0°'de ortalama güven < 0,5 ya da satır yoksa, küçültülmüş görüntüde 90/180/270 denenir; en yüksek
   güvenli yön seçilir. Satır düzeyinde metin yönü sınıflandırıcısı (cls) açık.
3. **Algılama + tanıma** (RapidOCR, PP-OCRv6 çok dilli; Türkçe ı/ğ/ş destekli — ölçüldü):
   - `hizli`: det small + rec small.
   - `uyarlamali` (varsayılan): det small + rec small; sonra güveni `< ikinci_esik` satırlar rec **medium** ile
     yeniden tanınır; det small mürekkepli sayfada az kutu bulursa (kutu alanı < sayfa mürekkep alanının %50'si)
     det **medium** ile yeniden algılanır.
   - `kalite`: det medium + rec medium, düşük güvenli satırlar ayrıca Tesseract'a.
   Ölçüm (RTX 3070 dizüstü, CPU onnxruntime, yarım A4): small/small 0,67 sn/sayfa, medium/medium 6,85 sn/sayfa.
4. **İkinci görüş**: Tesseract (`tur+eng`, `--psm 7`) varsa ve satır hâlâ `< ikinci_esik` ise satır kırpıntısı
   Tesseract'a verilir. Adaylar arasından **puan = güven + 0,15 × sözlükte bulunan sözcük oranı** en yüksek olan seçilir.
5. **Sözlük düzeltmesi** (`sozluk.py`): sözlük = `ocr.sozluk` dosyası (`sözcük sıklık` satırları, yoksa boş) +
   `terimler.txt` terimleri. Sözlükte olmayan ve harf içeren her sözcük için karışan harf çiftleri
   (`i/ı`, `I/İ`, `s/ş`, `S/Ş`, `g/ğ`, `G/Ğ`, `c/ç`, `C/Ç`, `o/ö`, `O/Ö`, `u/ü`, `U/Ü`) üzerinden en çok 64 varyant
   üretilir; sözlükte bulunan en sık varyant seçilir; büyük/küçük harf düzeni korunur. Sayı içeren sözcük, 2 harften
   kısa sözcük ve tamamen büyük harfli kısaltma (ör. `PRF`) değişmez. Türkçe küçük harf: `I→ı`, `İ→i`.
6. **Okuma sırası**: kutular satırlara (dikey örtüşme ≥ %50) toplanır; sütunlar, satır başlangıçlarının x
   dağılımındaki geniş boşlukla (sayfa genişliğinin ≥ %5'i) ayrılır; sütun sütun, yukarıdan aşağı okunur.
   Satırlar `\n`, büyük dikey boşluk (medyan satır yüksekliğinin 1,5 katı) boş satırla birleşir.
7. Sayfa güveni = satır güvenlerinin karakter sayısıyla ağırlıklı ortalaması.

### 5.5 Aranabilir PDF (`belge/pdf_katman.py`)
- PDF girdisi ve en az bir sayfası OCR'landıysa: özgün PDF kopyalanır, OCR'lı her sayfaya satır kutularına
  görünmez metin (`render_mode=3`) eklenir. Kutular `Donusum` ile özgün piksele, sonra sayfa noktasına
  (`72/dpi`, sayfa dönüşü `page.rotation` hesaba katılarak) çevrilir. Yazı boyutu kutu genişliğine sığdırılır.
  Yazı tipi: `C:\Windows\Fonts\arial.ttf` (yoksa PyMuPDF yerleşik `helv`). Çıktı: `aranabilir.pdf`
  (`garbage=3, deflate=True`).
- Görüntü girdisi: her görüntü/kare bir PDF sayfası olur (görüntü + görünmez metin).
- Diğer biçimlerde aranabilir PDF yok (metin zaten var).
- Doğrulama testi: üretilen PDF'den `get_text()` OCR metnindeki sözcükleri içermeli.

### 5.6 Belge işleme hattı (`belge/hat.py`)
`def isle_belge(yol: Path, depo: Path, ayar: Ayar, havuz: OcrHavuzu | None = None) -> BelgeKaydi`
1. `sha = sha256(dosya)` (1 MB'lık parçalarla akış). `hedef = depo/belgeler/sha[:2]/sha/`.
2. `hedef/belge.json` varsa ve `surum` uyuyorsa yeniden işlenmez (aynı içerik farklı yerde → tek işlem).
3. Sayfa başına önbellek `hedef/_ara/sayfa_00001.json`: iş yarıda kalırsa biten sayfalar yeniden OCR'lanmaz.
   İş bitince `_ara` silinir (`genel.ara_dosya_sakla` değilse).
4. OCR gereken sayfalar `OcrHavuzu`'na (ProcessPoolExecutor, `dizin.isci` süreç) **sayfa numarası + dosya yolu**
   olarak verilir; işçi PDF'yi kendisi açıp sayfayı işler (büyük görüntü süreçler arasında taşınmaz).
   Sayfa zaman aşımı → o sayfa `metin=''`, `uyarilar`'a yazılır, iş sürer.
5. Çıktılar (UTF-8, BOM'suz, `ensure_ascii=False`), geçici adla yazılıp `os.replace` ile:
   - `belge.json`:
     ```json
     {"surum": 1, "sha": "...", "kaynak_ad": "a.pdf", "bicim": "pdf", "sayfa_sayisi": 12, "ocr_sayfa": 3,
      "aranabilir_pdf": "aranabilir.pdf", "sure_sn": 41.2,
      "motor": {"ocr": "PP-OCRv6 uyarlamali", "tesseract": null},
      "sayfalar": [{"no": 1, "etiket": "Sayfa 1", "kaynak": "metin", "guven": null, "metin": "..."}],
      "uyarilar": []}
     ```
   - `metin.txt`: sayfalar `\f` (form feed) ile ayrılmış düz metin.
   - `aranabilir.pdf`: 5.5'e göre (yoksa `aranabilir_pdf: null`).

### 5.7 Dizinleyici (`dizin/vt.py`, `dizin/tarayici.py`, `dizin/yayin.py`, `dizin/dizinle.py`)
- `vt.py`: `ac(yol) -> sqlite3.Connection` (WAL, `foreign_keys=ON`), şemayı `kaynak/ortak/dizin_sema.sql`'den
  kurar (paket içinde `<paket kökü>\ortak\dizin_sema.sql`; `yollar.ortak(ad)` ile bulunur).
- **Tür kuralı**: klasör → `klasor`; 5.3 tablosundaki görüntü uzantıları → `goruntu`; diğer 5.3 uzantıları →
  `belge`; `.mp4 .mkv .mov .avi .m4a .wav .mp3` → `video`; geri kalan → `diger`.
  `durum`: belge/görüntü → `bekliyor` (sonra `tamam|hata|desteklenmiyor`); diğerleri → `yok`.
- **Ders bağı**: yol bir `*_islenmis` ya da `*_videoperisi` klasörünün içindeyse `ders = o klasörün tam yolu`;
  ya da `<ad>_belgeler` klasörünün içindeyse ve yanında `<ad>_islenmis` varsa `ders = <ad>_islenmis` yolu.
  Video dosyası `<stem>.mp4` için yanında `<stem>_islenmis` varsa `ders` o klasördür.
- **Tarama** (`tarayici.py`): `os.scandir` ile özyinelemeli; `haric` adları (büyük/küçük harf duyarsız) atlanır;
  erişim hatası günlüğe yazılır, tarama sürer. Satır `(yol)` anahtarıyla eklenir/güncellenir; `(boyut, degisme)`
  değişen belge `bekliyor`a döner ve sayfaları silinir; diskte artık olmayan satırlar silinir (sayfalarıyla).
  1000 satırlık işlemlerle commit. Önce yalnız katalog taranır ve **hemen yayınlanır** (ad araması içerik
  bitmeden çalışsın).
- **İçerik** (`dizinle.py`): `bekliyor` satırlar `boyut` küçükten büyüğe işlenir; `max_dosya_mb` üstü →
  `desteklenmiyor` ("Dosya çok büyük"). Her belge `isle_belge` → `sayfa` satırları (eskiler silinip) yazılır →
  `dosya.durum/sha/sayfa_sayisi/ocr_sayfa` güncellenir → commit. Hata → `durum='hata'`, `hata=<Türkçe kısa neden>`;
  sonraki taramada dosya değişmediyse yeniden denenmez.
- **Yayın** (`yayin.py`): `VACUUM INTO '<depo>\yayin\dizin-YYYYmmdd-HHMMSS.db.tmp'` → `os.replace` ile `.db` →
  `guncel.txt` (yalnız dosya adı, tek satır) geçici dosya + `os.replace` ile yazılır. `sakla_yayin`'dan eski
  anlık görüntüler silinmeye çalışılır (kilitliyse sessizce kalır). Yayın: tarama sonunda, her `yayin_dk`
  dakikada bir ve içerik bitince.
- **Komutlar** (`__main__.py`):
  - `python -m videohat dizinle [--kok K]... [--depo D] [--bir-kez] [--yalniz-ad] [--profil hizli|uyarlamali|kalite]`
    — varsayılan izleme kipidir (tara → işle → yayınla → `tarama_dk` bekle); Ctrl+C ile düzgün durur.
  - `python -m videohat belge <dosya|klasör>... [--cikis K] [--profil ...]` — tek başına belge işleme:
    çıktı `<ad>_belge\` klasörüne (`belge.json`, `metin.txt`, `aranabilir.pdf`).
  - Çıkış kodları mevcut düzenle aynı: 0 ok, 1 iş hatası, 2 ortam hatası.
- **İzleme entegrasyonu** (`izle.py`): `is\gelen\`e atılan belge/görüntü `belge` komutuyla işlenir, sonuç
  `is\bitti\<ad>_belge\` klasörüne gider; mevcut video akışı değişmez.
- **Bat dosyaları**: `kaynak/bat/dizinle.bat` (izleme kipi), `kaynak/bat/belge.bat` (üstüne sürükle-bırak).

## 6. Bulgu Portalı (C#)

### 6.1 Çekirdek (`Portal.Cekirdek/Dizin/`, paket `Microsoft.Data.Sqlite` 9.0.4)
- `DizinNormalizasyon`: `TrNormalizasyon.Normalle`'yi kullanır (yeni normalizasyon yazılmaz).
- `DizinKaynagi`: `dizin` klasörünü alır; `yayin\guncel.txt`'yi okur; anlık görüntüyü
  `%LOCALAPPDATA%\Bulgu\dizin\` altına kopyalar (zaten aynı ad ve boyutta varsa kopyalamaz), `Mode=ReadOnly` açar.
  `Yenile()` guncel.txt değiştiyse yeni kopyayı arka planda hazırlar ve bağlantıyı değiştirir; eski kopyaları siler.
  Durum: `Hazir | Yok | Hata` + Türkçe ileti ("Dizin bulunamadı. Bulgu İşleyici'de dizinle.bat'ı çalıştırın.").
  Şema sürümü `meta.surum != 1` → `Hata`.
- `DosyaSorgusu.Ayristir(string)` — Everything benzeri sözdizimi (anahtar sözcükler Türkçe ve İngilizce):
  - boşlukla ayrılmış terimler VE; `|` VEYA (terimler arası); `!terim` DEĞİL; `"tırnaklı ifade"` tek terim;
  - `*` ve `?` içeren terim tüm adla eşleşir (joker); jokersiz terim adın içinde alt dizedir;
  - `uz:pdf;docx` / `ext:pdf;docx`; `tur:belge|goruntu|video|klasor|diger` / `type:`;
  - `boyut:>10mb`, `boyut:<500kb`, `boyut:1mb..5mb` / `size:` (birimler b, kb, mb, gb; 1024 tabanı);
  - `tarih:2026`, `tarih:2026-10`, `tarih:>2026-01-01`, `tarih:2026-01-01..2026-03-31` / `dm:` (değişme tarihi,
    yerel saat);
  - `yol:metin` / `path:metin` → terim ad yerine tam yolda aranır;
  - `icerik:metin` / `content:metin` → yalnız içeriğinde bu sözcükler geçen belgeler;
  - `ders:` → yalnız bir derse bağlı dosyalar; `!ders:` → bağsızlar.
  Bilinmeyen `x:` öneki düz terimdir. Ayrıştırıcı saf C#'tır, veritabanı gerektirmez.
- `DosyaAramaMotoru.Ara(DosyaSorgusu, Siralama, int enFazla = 1000) -> IReadOnlyList<DosyaSonucu>`:
  SQL'i parametreli kurar (dize birleştirme YOK). 3+ karakterli alt dize terimi → `dosya_fts MATCH` (trigram;
  ifade `"..."` tırnaklı, içteki `"` ikilenir) + `norm_ad LIKE` doğrulaması; 1-2 karakter → yalnız `LIKE`
  (`ESCAPE '\'`). Joker → `norm_ad GLOB` (joker dışı karakterler normalize edilir). Sıralama: `Ilgi` (tam ad
  eşleşmesi > ad başı > içerir, sonra ad), `Ad`, `Yol`, `Boyut`, `Tarih` (artan/azalan).
  `DosyaSonucu(long Id, string Yol, string Ad, string Klasor, string Uzanti, long Boyut, DateTime Degisme,
  string Tur, string Durum, int? SayfaSayisi, string? Ders)`.
- `IcerikAramaMotoru.Ara(string sorgu, DosyaSorgusu? suzgec, int enFazla = 200) -> IReadOnlyList<IcerikSonucu>`:
  sorgu `TrNormalizasyon.Kelimeler` ile sözcüklere bölünür; FTS5 ifadesi her sözcük için `"sozcuk"*` (VE);
  sorguda 2+ sözcük varsa tam ifade (`"s1 s2"`) eşleşmesi önce gelir (mevcut motordaki 1. kademe gibi), sonra
  `bm25`. Süzgeç (uz/tarih/yol/tur/ders) `dosya` tablosuna JOIN ile uygulanır. Belge başına en çok 5 sayfa.
  `IcerikSonucu(long DosyaId, string Yol, string Ad, int SayfaNo, string Etiket, string Kaynak, double? Guven,
  string Alinti, (int Bas, int Uzunluk)[] Vurgular, double Puan, string? Ders)`.
  `Alinti`: özgün sayfa metninden ilk eşleşmenin çevresinde en çok 240 karakter (sözcük sınırında, başta/sonda
  `…`); `Vurgular` alıntı içindeki konumlardır. Normalize konumdan özgün konuma geçiş için `MetinEsleme`
  yardımcısı: özgün metni karakter karakter normalize ederken her normalize karakterin özgün indeksini tutar.
- `SayfaDeposu.Getir(long dosyaId) -> IReadOnlyList<SayfaKaydi>` ve `Getir(long dosyaId, int no)` —
  görüntüleyici için. `BelgeYollari.AranabilirPdf(depo, sha)` → `<depo>\belgeler\sha[:2]\sha\aranabilir.pdf`
  varsa onu döner.
- `DersBelgeleri(string dersKlasoru) -> IReadOnlyList<DosyaSonucu>` — `ders = @yol` olan dosyalar.
- `BirlesikArama`: `IAramaMotoru` (video), `DosyaAramaMotoru`, `IcerikAramaMotoru`'nu tek çağrıda koşar;
  `BirlesikSonuc(SonucTuru Tur, ...)` listesi döner (her türden en çok N, varsayılan 50). Dizin yoksa yalnız video.
  İptal edilebilir (`CancellationToken`), yazarken her tuşta önceki arama iptal edilir (200 ms gecikme).

### 6.2 Arayüz (`Portal.Uygulama`)
- `TargetFramework` → `net8.0-windows10.0.19041.0` (PDF işleme için `Windows.Data.Pdf`); `SupportedOSPlatformVersion`
  `10.0.17763.0`.
- Pencere başlıkları: `Bulgu — Arama`, `Bulgu — Oynatıcı`, `Bulgu — Belge`. Logo/ikon aynı kalabilir.
- **Arama penceresi**: arama kutusunun altında sekme şeridi: `Tümü`, `Dosyalar`, `Belgeler`, `Videolar` (sayılarıyla).
  - `Dosyalar`: sanallaştırılmış `ListView` + `GridView` (Ad, Klasör, Boyut, Değişme, Tür); sütun başlığına
    tıklayınca sıralama; çift tık/Enter → kabukla aç (`Process.Start` + `UseShellExecute`); sağ tık menüsü:
    `Aç`, `Klasörünü aç` (`explorer.exe /select,"yol"`), `Yolu kopyala`, durum `tamam` ise `Belgede göster`.
  - `Belgeler`: belge başına grup başlığı (ad, klasör, ders bağı varsa rozet) + sayfa sonuçları (etiket, alıntı
    vurgulu, OCR ise `OCR %güven` rozeti). Tıklayınca belge görüntüleyici o sayfada açılır.
  - `Videolar`: mevcut sonuç listesi aynen.
  - `Tümü`: üç bölüm, her biri ilk 10 sonuç + `Tümünü göster` bağlantısı (ilgili sekmeye geçer).
  - Alt durum çubuğu: dizin durumu ("Dizin: 182.344 dosya, 412.003 sayfa — 30.10.2026 07:40").
- **Belge görüntüleyici** (`BelgePenceresi`): üst çubuk (önceki/sonraki sayfa, sayfa no kutusu, yakınlaştırma,
  belge içi arama + sonraki/önceki eşleşme, `Aslını aç`, `Klasörünü aç`). Gövde iki bölme: solda sayfa görüntüsü
  (PDF ya da görüntü belgelerinde; `aranabilir.pdf` varsa o, yoksa özgün; `Windows.Data.Pdf` ile arka planda,
  sayfa başına önbellek), sağda sayfa metni (`sayfa.metin`, eşleşmeler vurgulu; OCR sayfasında güven rozeti).
  Görüntülenemeyen biçimlerde yalnız metin bölmesi tam genişlikte. 1000+ sayfada yalnız görünen sayfa işlenir.
- **Oynatıcı**: derse bağlı belge varsa üst çubukta `Belgeler (n)` düğmesi → liste → görüntüleyici.
- Erişilebilirlik ve tema: mevcut `Temalar` kaynakları ve klavye odağı düzeni korunur.

## 7. Testler (kabul ölçütü)

Python (`kaynak/tests/test_belge_*.py`, `test_dizin_*.py`; `python -m unittest discover -s kaynak/tests`):
- normalizasyon vakaları (ortak JSON) birebir;
- her biçim için çıkarma: test içinde üretilen DOCX (sayfa sonu + tablo), PPTX (2 slayt + not), XLSX (450 satır →
  3 parça), ODT, RTF (`\'fd` = ı), TXT (cp1254 ve utf-16), HTML, EML (ek olarak TXT), metinli PDF;
- OCR (modeller yoksa `skipTest`): PyMuPDF ile Arial'la çizilmiş Türkçe sayfa → OCR metninde `frekansı`, `ölçümü`,
  `güvenlik` sözcükleri; 2° döndürülmüş ve 90° çevrilmiş sayfa da okunur; aranabilir PDF'den geri okunan metin
  aynı sözcükleri içerir;
- sözlük düzeltmesi: `yapilmalidir`→`yapılmalıdır` (sözlükte varsa), `PRF` değişmez, sayı içeren sözcük değişmez;
- okuma sırası: iki sütunlu yapay kutu kümesi doğru sırada birleşir;
- sayfa önbelleği: işçi 3. sayfada kesilince yeniden çağrıda 1-2 OCR'lanmaz (sahte motor ile sayaç);
- dizinleyici: geçici ağaçta tarama → satır sayıları, tür, ders bağı kuralları (3 kural); dosya değişince yeniden
  işleme; silinince satır ve sayfaları kalkar; aynı içerikli iki dosya → tek `belge.json`; yayın → `guncel.txt`
  geçerli `.db`'yi gösterir ve `sakla_yayin` uygulanır; FTS sorgusu (`dosya_fts MATCH`, `sayfa_fts MATCH`) sonuç verir.

C# (`Portal.Testleri`):
- normalizasyon vakaları (ortak JSON; test projesine bağlantılı içerik olarak kopyalanır);
- `DosyaSorgusu` ayrıştırıcı: her önek, birim, aralık, VE/VEYA/DEĞİL, tırnak, joker, bilinmeyen önek;
- `DosyaAramaMotoru` / `IcerikAramaMotoru`: test, `dizin_sema.sql` ile geçici veritabanı kurar, örnek satırlar ekler;
  Türkçe karakter duyarsızlığı (`ışık` sorgusu `ISIK.pdf`'yi bulur), 1-2 karakterli sorgu, joker, süzgeçler,
  sıralama, tam ifade önceliği, belge başına 5 sayfa sınırı, alıntı ve vurgu konumları (Türkçe harfli metinde);
- `DizinKaynagi`: guncel.txt yok → `Yok`; var → yerel kopya ve `Hazir`; guncel.txt değişince `Yenile` yeni dosyayı
  açar; şema sürümü farklı → `Hata`;
- `BirlesikArama`: dizin yokken yalnız video sonuçları; iptal.
Mevcut tüm testler geçmeye devam eder.

## 8. Kapsam dışı (bu sürümde yok)
- El yazısı tanıma için ayrı model; yapılandırılmış tablo çıkarımı (tablolar satır metni olarak gelir).
- ZIP/7z arşivlerinin içi (arşiv dosyasının adı dizinlenir).
- Gerçek zamanlı dosya sistemi bildirimi (USN günlüğü); tarama aralıklıdır (`tarama_dk`).
- Kullanıcı bazlı erişim denetimi: portal dizindeki her dosyayı gösterir. Dizinlenecek kökler buna göre seçilmelidir.
