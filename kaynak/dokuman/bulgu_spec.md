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
yayin_dk = 30               # en geç bu kadar dakikada bir yayın (yalnız değişiklik varsa)
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

## 4. Normalizasyon (iki dilde aynı) — iki ayrı işlev

**4.1 İçerik normalizasyonu** (`sayfa_fts`, video araması). `TrNormalizasyon.Normalle` (C#, mevcut) referanstır;
iki küçük ek ile (C# tarafında da uygulanır): **birleştirici işaretler (kategori Mn) atılır** (boşluk da olmaz:
`i̇` → `i`) ve **BMP dışı karakter** (C#'ta vekil çift, Python'da `ord > 0xFFFF`) → boşluk.
Python karşılığı `videohat/dizin/normal.py: icerik_normalle(s)`:
- `ı İ I i î Î → i`, `ş Ş → s`, `ğ Ğ → g`, `ü Ü û Û → u`, `ö Ö → o`, `ç Ç → c`, `â Â → a`;
- `' ’ ‘ \` ´` → boşluk; A-Z → küçük harf;
- Unicode kategorisi P* (noktalama), S* (simge), Z* ya da `\t\n\v\f\r\x85\xa0` (boşluk), Cc (denetim) → boşluk;
- Mn → atılır; BMP dışı → boşluk;
- diğer karakter → `c.lower()` (sonuç tek karakter değilse `c` kalır);
- ardışık boşluk teke iner, baş ve son boşluk atılır.
Vakalar: `kaynak/ortak/normalizasyon_vakalari.json` (her iki test takımında koşulur).

**4.2 Dosya adı normalizasyonu** (`dosya.norm_ad`, `dosya.norm_yol`). Noktalama, ayraç ve boşluk KORUNUR
(`.pdf`, `v1.2`, `a_b`, `\` araması çalışsın). Python `dosya_normalle(s)`, C# `DosyaNormalizasyon.Normalle(s)`:
Türkçe harf katlaması 4.1'deki gibi; A-Z ve diğer harfler küçük harfe; Mn atılır; başka hiçbir karakter
değişmez, boşluk daraltılmaz. Vakalar: `kaynak/ortak/dosya_normalizasyon_vakalari.json`.

**4.3 NFKC**: çıkarılan sayfa metni (`sayfa.metin`) saklanmadan ÖNCE Python'da `unicodedata.normalize("NFKC")`
ile düzenlenir (PDF bağlaçları `ﬁ` → `fi`, ayrışık harfler birleşir). Böylece C# tarafında görünen metin ile
normalize metin arasındaki eşleme (MetinEsleme) karakter karakter kurulabilir.

## 5. Bulgu İşleyici (Python) — yeni modüller

Paket: `kaynak/app/videohat/`. Yeni alt paketler `belge/` ve `dizin/`. Mevcut kod üslubu: Türkçe ad, kısa yorum,
`logging.getLogger("videohat")`, hatalar `hatalar.py`'deki sınıflarla (`IsHatasi` vb.).

### 5.1 Bağımlılıklar (paketleyici `hazirla.ps1` kurar)
`pymupdf`, `rapidocr` (onnxruntime ile; onnxruntime zaten var), `opencv-python-headless`, `numpy`, `pillow`,
`python-docx`, `python-pptx`, `openpyxl`, `olefile`. İsteğe bağlı: `extract-msg` (MSG e-posta). Eski ikili Office
(`.doc .xls .ppt`) için `araclar\libreoffice\program\soffice.exe` varsa `--headless --convert-to` ile çevrilir;
yoksa `durum='desteklenmiyor'`, `hata='Eski Office biçimi: LibreOffice bulunamadı'`. LibreOffice her çağrıda
geçici profil alır (`-env:UserInstallation=file:///<ara>/lo_profil_<pid>`) ve 120 sn zaman aşımıyla koşar
(aşımda süreç ağacı öldürülür, `durum='hata'`).

**OCR modelleri çevrimdışı:** RapidOCR modelleri internetten indirmeye ÇALIŞMAMALI. Modeller
`<paket kökü>\modeller-ocr\` altındadır ve motora açık yolla verilir (`Det.model_path`, `Rec.model_path`,
`Cls.model_path`, gerekirse `Rec.rec_keys_path`). Dosyalar: `PP-OCRv6_det_small.onnx`, `PP-OCRv6_det_medium.onnx`,
`PP-OCRv6_rec_small.onnx`, `PP-OCRv6_rec_medium.onnx`, `ch_ppocr_mobile_v2.0_cls_mobile.onnx`.
`yollar.py`'ye `ocr_model(ad) -> Path` (yoksa `OrtamHatasi`) ve `tesseract() -> Path | None` eklenir.

### 5.2 Veri modeli (`belge/model.py`)
```python
@dataclass
class OcrSatir: metin: str; guven: float; kutu: tuple[float, float, float, float]
# kutu: x0,y0,x1,y1 — ÖZGÜN (ön işleme öncesi, işlenen sayfa görüntüsünün) pikselinde. Ön işlemedeki ölçek,
# eğiklik döndürmesi (tuval genişleme ofseti dahil) ve yön dönüşü OcrMotoru.oku içinde geri çevrilir.
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
`def cikar(yol: Path, ayar: Ayar, havuz: OcrHavuzu | None, ara: Path) -> BelgeSonucu` — uzantıya göre dağıtır
(`havuz=None` → OCR gereken sayfa `metin=''` + uyarı). Her sayfa metnine 4.3 (NFKC) uygulanır:

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
     yeniden tanınır; det small mürekkepli sayfada az kutu bulursa (kutu alanı < sayfa *metin benzeri* mürekkep
     alanının %50'si) det **medium** ile yeniden algılanır. Metin benzeri mürekkep: bağlı bileşen analizinde
     yüksekliği 6–120 piksel ve en-boy oranı 0,1–15 olan bileşenler; büyük görsel, fotoğraf, tablo çizgisi ve
     tarama kenarı bu hesaba girmez (resimli sayfa gereksiz yere yavaş modele düşmesin).
   - `kalite`: det medium + rec medium, düşük güvenli satırlar ayrıca Tesseract'a.
   Ölçüm (RTX 3070 dizüstü, CPU onnxruntime, yarım A4): small/small 0,67 sn/sayfa, medium/medium 6,85 sn/sayfa.
4. **İkinci görüş**: Tesseract (`tur+eng`, `--psm 7`) varsa ve satır hâlâ `< ikinci_esik` ise satır kırpıntısı
   Tesseract'a verilir. Adaylar arasından **puan = güven + 0,15 × sözlükte bulunan sözcük oranı** en yüksek olan seçilir.
5. **Sözlük düzeltmesi** (`sozluk.py`): sözlük = `ocr.sozluk` dosyası (`sözcük sıklık` satırları, yoksa boş) +
   `terimler.txt` terimleri. Bellekte `icerik_normalle(sözcük küçük harf) → en sık yazım` indeksi kurulur (O(1)
   bakış; varyant üretimi YOK). Düzeltme YALNIZ güveni `< 0,85` olan satırlarda ve yalnız sözlükte birebir
   bulunmayan sözcükte uygulanır: sözcüğün normalize anahtarı indekste varsa ve aday ile OCR çıktısı yalnız
   karışan harf çiftlerinde (`i/ı`, `I/İ`, `s/ş`, `g/ğ`, `c/ç`, `o/ö`, `u/ü`, büyük harfleri dahil) ayrışıyorsa aday
   yazılır; büyük/küçük harf düzeni korunur. Sayı içeren sözcük, 2 harften kısa sözcük ve tamamen büyük harfli
   kısaltma (ör. `PRF`) değişmez. Türkçe küçük harf: `I→ı`, `İ→i`.
6. **Okuma sırası** (XY-cut): önce sütun blokları ayrılır — kutuların yatay izdüşümünde sayfa yüksekliğinin
   ≥ %60'ı boyunca kesintisiz boş kalan ve genişliği ≥ sayfa genişliğinin %2'si olan dikey oluk sütun sınırıdır
   (tam genişlik başlıklar oluğu kesiyorsa sayfa önce yatay boşluklarla şeritlere bölünür, her şeritte oluk aranır).
   Sonra HER BLOK İÇİNDE kutular satırlara (dikey örtüşme ≥ %50) toplanır, satır içi soldan sağa. Bloklar soldan
   sağa, şeritler yukarıdan aşağı okunur. Girinti ya da ortalanmış başlık sütun sayılmaz (oluk sayfa boyu
   kesintisiz olmalı). Satırlar `\n`, büyük dikey boşluk (medyan satır yüksekliğinin 1,5 katı) boş satırla birleşir.
7. Sayfa güveni = satır güvenlerinin karakter sayısıyla ağırlıklı ortalaması.

### 5.5 Aranabilir PDF (`belge/pdf_katman.py`)
- PDF girdisi ve en az bir sayfası OCR'landıysa: özgün PDF kopyalanır, OCR'lı her sayfaya satır kutularına
  görünmez metin (`render_mode=3`) eklenir. Kutular (zaten özgün piksel) önce `72/dpi` ile işlenmiş
  görüntü noktasına, sonra `page.rotation != 0` ise `page.derotation_matrix` ile sayfanın döndürülmemiş
  koordinatına çevrilir; metin `rotate=page.rotation` ile yazılır. Test: 90° döndürülmüş PDF sayfasında geri okunan
  sözcüklerin kutusu OCR kutusuyla örtüşür. Yazı boyutu kutu genişliğine sığdırılır.
  Yazı tipi: `C:\Windows\Fonts\arial.ttf` (yoksa PyMuPDF yerleşik `helv`). Çıktı: `aranabilir.pdf`
  (`garbage=3, deflate=True`).
- Görüntü girdisi: her görüntü/kare bir PDF sayfası olur (görüntü + görünmez metin).
- Diğer biçimlerde aranabilir PDF yok (metin zaten var).
- Doğrulama testi: üretilen PDF'den `get_text()` OCR metnindeki sözcükleri içermeli.

### 5.6 Belge işleme hattı (`belge/hat.py`)
`def isle_belge(yol: Path, depo: Path, ayar: Ayar, havuz: OcrHavuzu | None = None) -> BelgeKaydi`
1. `sha = sha256(dosya)` (1 MB'lık parçalarla akış). `hedef = depo/belgeler/sha[:2]/sha/`.
2. `hedef/belge.json` varsa ve `surum` uyuyorsa yeniden işlenmez (aynı içerik farklı yerde → tek işlem).
3. Sayfa başına önbellek `hedef/_ara/sayfa_00001.json` = `Sayfa`'nın tamamı (`no, etiket, kaynak, metin, guven,
   satirlar[{metin, guven, kutu}], gorsel_boyut, dpi`): iş yarıda kalırsa biten sayfalar yeniden OCR'lanmaz ve
   aranabilir PDF önbellekten de eksiksiz kurulur. İş bitince `_ara` silinir (`genel.ara_dosya_sakla` değilse).
4. `OcrHavuzu` (kendi süreç havuzu: `multiprocessing` ile `dizin.isci` işçi süreç + iş/sonuç kuyrukları;
   `ProcessPoolExecutor` KULLANILMAZ çünkü takılan işçi öldürülemez). İş = **dosya yolu + sayfa numarası** (ya da
   görüntü/geçici dosya yolu); işçi dosyayı kendisi açıp sayfayı işler (büyük görüntü süreçler arasında taşınmaz),
   motoru süreç başına bir kez yükler. Sayfa `ocr.sayfa_zaman_asimi_sn`'yi aşarsa o işçi `terminate()` edilir,
   yerine yenisi açılır; sayfa `metin=''` olur, `uyarilar`'a yazılır, iş sürer. `isci=1` iken havuzsuz (aynı
   süreçte, zaman aşımı yok) çalışır — testler bunu kullanır; zaman aşımı testi sahte yavaş işçiyle 2 işçide yapılır.
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
  değişen belge `bekliyor`a döner ve sayfaları silinir. Diskte artık olmayan satırlar silinir (sayfalarıyla)
  — **VERİ KAYBI KORUMASI**: `os.scandir`/`stat` hatası veren klasörün altındaki mevcut satırlar silme kümesinden
  muaftır (ağ kopması kaydı silmesin); bir kök hiç açılamazsa o kökün satırlarına dokunulmaz ve o turda yayın
  yapılmaz. Silinecek satır, kökteki satırların %50'sinden fazlaysa silme yapılmaz, günlüğe uyarı yazılır
  (`--zorla-sil` ile aşılır). Silme, tarama tamamen bittikten sonra yapılır (görülen yollar geçici tabloda tutulur,
  tüm ağaç belleğe alınmaz). 1000 satırlık işlemlerle commit. Önce yalnız katalog taranır ve **hemen yayınlanır**
  (ad araması içerik bitmeden çalışsın).
- **İçerik** (`dizinle.py`): `bekliyor` satırlar `boyut` küçükten büyüğe işlenir; `max_dosya_mb` üstü →
  `desteklenmiyor` ("Dosya çok büyük"). Her belge `isle_belge` → `sayfa` satırları (eskiler silinip) yazılır →
  `dosya.durum/sha/sayfa_sayisi/ocr_sayfa` güncellenir → commit. Hata → `durum='hata'`, `hata=<Türkçe kısa neden>`;
  sonraki taramada dosya değişmediyse yeniden denenmez.
- **Yayın** (`yayin.py`): `VACUUM INTO` YEREL geçici dosyaya (çalışma veritabanının yanında) → bütünlük denetimi
  (`PRAGMA quick_check`) → ağdaki `<depo>\yayin\dizin-YYYYmmdd-HHMMSS-<6 hane sıra>.db.tmp`'ye sıralı dosya kopyası
  (`shutil.copyfile`) → `os.replace` ile `.db` → `guncel.txt` (yalnız dosya adı, tek satır) geçici dosya +
  `os.replace` ile yazılır. Son yayından beri veritabanında değişiklik yoksa yayın atlanır. `sakla_yayin`'dan eski
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
- İçerik normalizasyonu `TrNormalizasyon.Normalle` (4.1'deki iki ek bu sınıfa eklenir: Mn atılır, vekil çift →
  boşluk); dosya adı normalizasyonu yeni `DosyaNormalizasyon.Normalle` (4.2).
- `DizinKaynagi`: `dizin` klasörünü alır; `yayin\guncel.txt`'yi okur; anlık görüntüyü
  `%LOCALAPPDATA%\Bulgu\dizin\` altına önce `<ad>.tmp`'ye kopyalar, boyut doğrulanınca `File.Move` ile `<ad>`
  yapar (aynı adlı tam kopya zaten varsa kopyalamaz). Bağlantı `Mode=ReadOnly;Pooling=False` ile **sorgu başına**
  açılır (paylaşılan uzun ömürlü bağlantı yok; dosya kilidi kalmaz). `Yenile()` guncel.txt değiştiyse yeni
  kopyayı arka planda hazırlar, sonra etkin dosya yolunu atomik değiştirir; çalışan sorgu eski dosyada biter;
  eski kopyalar silinmeye çalışılır (kilitliyse sonraki `Yenile`'de).
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
  SQL'i parametreli kurar (dize birleştirme YOK). Terimler `DosyaNormalizasyon` ile normalize edilir; uzunluk
  kontrolü normalize SONRASI yapılır. 3+ karakterli alt dize terimi → `dosya_fts MATCH` (trigram; ifade `"..."`
  tırnaklı, içteki `"` ikilenir); 1-2 karakter → yalnız `norm_ad LIKE` (`ESCAPE '\'`). `yol:` terimi →
  `norm_yol LIKE` (FTS yok). Joker → `norm_ad GLOB` (joker dışı karakterler normalize edilir; `*.pdf` yalnız
  `.pdf` ile biten adları eşler). `icerik:` süzgeci `id IN (SELECT ...)` alt sorgusuyla (tekrarlı satır yok). Sıralama: `Ilgi` (tam ad
  eşleşmesi > ad başı > içerir, sonra ad), `Ad`, `Yol`, `Boyut`, `Tarih` (artan/azalan).
  `DosyaSonucu(long Id, string Yol, string Ad, string Klasor, string Uzanti, long Boyut, DateTime Degisme,
  string Tur, string Durum, int? SayfaSayisi, string? Ders)`.
- `IcerikAramaMotoru.Ara(string sorgu, DosyaSorgusu? suzgec, int enFazla = 200) -> IReadOnlyList<IcerikSonucu>`:
  sorgu `TrNormalizasyon.Kelimeler` ile sözcüklere bölünür; FTS5 ifadesi her sözcük için `"sozcuk"*` (VE;
  `sayfa_fts`'in `prefix='2 3'` dizini önek aramasını hızlandırır). Sorguda 2+ sözcük varsa tam ifade
  (`"s1 s2"`) eşleşmesi önce gelir (mevcut motordaki 1. kademe gibi), sonra `bm25`. `bm25(sayfa_fts)` YALNIZ en içteki
  FTS sorgusunda sütun olarak seçilir (pencere işlevi içinde çağrılamaz); belge başına en çok 5 sayfa sınırı dış
  sorguda `ROW_NUMBER() OVER (PARTITION BY dosya_id ORDER BY puan)` ile uygulanır, `LIMIT enFazla` en sonda.
  Süzgeç (uz/tarih/yol/tur/ders) `dosya` tablosuna JOIN ile uygulanır. Görüntülenen metin `sayfa.metin`'dir
  (normalize metin saklanmaz; içeriksiz FTS).
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
  (PDF ya da görüntü belgelerinde; `aranabilir.pdf` varsa o, yoksa özgün; dosya önce `%LOCALAPPDATA%\Bulgu\gorunum\`
  altına kopyalanır — ağdaki dosya açık tutulup kilitlenmesin; `Windows.Data.Pdf` ile arka planda,
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

## 9. Uygulamada eklenen kararlar ve ölçümler (2026-10-30)
- **Sözlük düzeltmesi** yalnız işaret EKLEYEN değişime izin verir (`i→ı`, `s→ş`, `g→ğ`, `c→ç`, `o→ö`, `u→ü`, `I→İ`);
  OCR işareti düşürür, nadiren ekler. Güven eşiği 0,99 (ölçüldü: hatalı satırların güveni 0,96–0,98 çıktı).
  Büyük harfli sözcükteki küçük `i/ı` → `İ/I`. Yabancı işaretli harf (`ś í ű ů ő …`) taban harfe iner; `ș` → `ş`.
- **Ünlü uyumu** (`ocr.unlu_uyumu`, varsayılan açık): sözlükte olmayan çekimli sözcükte en uzun sözlük kökünden sonraki
  ekte `ı/i` ünlü uyumuyla belirlenir; ekte `ki`, `yor`, `ken` varsa uygulanmaz. Ölçüldü (tr_50k, 27.015 sözcük,
  ı→i bozulması benzetildi): %79,9 düzeltir, doğru sözcüğün %3,6'sını bozar.
- **Yön**: satır sınıflandırıcısı (cls) ters satırı da okuduğu için 0°/180° ayrımı cls kapalı güvenle yapılır
  (ölçüldü: aksi halde 90°/270°/180° taramada satır sırası tersine dönüyordu). Her sayfada küçültülmüş görüntüde 2 ek okuma.
- **Transkript dosyaları**: `*_islenmis` / `*_videoperisi` klasörünün doğrudan içindeki `.json .srt .vtt .txt` → `tur='diger'`
  (içerikleri video aramasında var; belge olarak işlenince ham JSON sonuçlara karışıyordu — ölçüldü).
- **Yazar ek dizini** `ix_dosya_yol_nocase` (yalnız yazar kullanır): büyük/küçük harfi değişen yolu tam tarama olmadan bulur.
- Ölçümler (RTX 3070 dizüstü, CPU onnxruntime, uyarlamalı profil): 12 sayfalık bozuk tarama kıyası (150 dpi, bulanık,
  1,5° eğik, JPEG 45) sözcük doğruluğu: sözlüksüz %88,3, sözlük %89,3, sözlük + ünlü uyumu %89,4; tek süreçte ~10 sn/sayfa.
  Tek sayfalık taranmış yönerge: %69,0 → %96,6. Kıyas metnindeki ASCII kod adları (`islenmis`) Türkçeye çevrildiği için
  hata sayılır; gerçek doğruluk bu sayıdan yüksektir (tahmin, ölçülmedi).

## 10. Bilinen sınırlar (bağımsız incelemede bulundu, bu sürümde açık)
- Yolda yalnız Türkçe harfin büyük/küçük harfi değişirse (`ö/Ö`) çift kayıt oluşabilir (NOCASE yalnız ASCII katlar).
- Yeniden başlatmadan sonra değişmemiş veritabanı bir kez gereksiz yayınlanabilir (yayin.json özeti).
- Eğiklik düzeltmesi 90°/270° yön bulunmadan önce de çalışır; dönük + eğik sayfada açı sapabilir.
- Uzun metin sayfası dağıtımı sırasında geçen süre OCR işçisinin zaman aşımı süresinden düşer.
- ODS'te 200'den az ardışık boş satır sonunda boş parça üretilebilir.

## 11. Sürüm 2 eki (kullanıcı isteği 2026-10-30): adlar, eski biçimler, ArtCraft ile açma, simgeler

### 11.1 Adlar (kullanıcının seçimi)
- Veri işleme parçası (Python; video transkript, belge, OCR, dizinleme): **Veri Fırını**.
- Veri sunumu parçası (WPF; video izleme, belge/dosya bulma): **Veri Dedektifi**.
- Kullanıcıya görünen "Bulgu", "Bulgu İşleyici", "Bulgu Portalı" metinlerinin hepsi bu iki ada döner. Pencere başlıkları:
  `Veri Dedektifi — Arama`, `Veri Dedektifi — Oynatıcı — {ad}`, `Veri Dedektifi — Belge — {ad}`. Menü başlığı: `Veri Fırını`.
- Depo klasörü varsayılanı `<kök>\_bulgu` → `<kök>\_veridedektifi`. Yerel klasör `%LOCALAPPDATA%\Bulgu` → `%LOCALAPPDATA%\VeriDedektifi`.
- Paket adları: `dist\VeriFirini-Paketi` (+`.7z`), `dist\VeriDedektifi` (+`.7z`). İç kod adları (videohat, Portal.*) DEĞİŞMEZ.

### 11.2 Eski biçimler (2000 ve öncesi belgeler)
Dönüştürücüler (bulunduğu sırayla denenir): paketteki `araclar\libreoffice\program\soffice.exe`, sistemdeki
`%ProgramFiles%\LibreOffice\program\soffice.exe` (ve x86), sonra ArtCraft CLI (`wordcraft-cli convert` .doc için,
`gridcraft-cli convert` .xls için; yalnız varsa). Paket `araclar\libreoffice` taşır: 26.8.2, `msiexec /a` ile ayıklanmış ve kırpılmış (684 MB; yazım sözlükleri, tr dışı arayüz dilleri ve yardım yok; VC++ DLL'leri `program\` yanında). Ölçüldü: sözlük klasörü varken Writer taze profilde 120 sn+ askıda kaldı, kırpılmış kopya .doc/.xls/.ppt'yi 4–5 sn'de çevirdi. Her LibreOffice çağrısı geçici profil + 120 sn zaman aşımı (5.1). Paket `araclar\7z` (7-Zip 26.05, 7z.exe+7z.dll) ve `araclar\djvulibre` (3.5.29, djvutxt/djvused/ddjvu + 32 bit VC++ DLL'leri) taşır.

| Grup | Uzantılar | Yol |
|---|---|---|
| Metin işlemci | `.doc .dot .wpd .wps .wri .lwp .sam .sxw .stw .sdw .mcw .602 .hwp .abw .zabw .cwk` | LibreOffice → `.docx` → mevcut DOCX çıkarıcı |
| Hesap tablosu | `.xls .xlt .xlw .xlsb .wk1 .wk3 .wk4 .123 .wq1 .wb2 .sxc .stc .sdc .dbf .dif .slk` | LibreOffice → `.xlsx` → XLSX çıkarıcı |
| Sunum | `.ppt .pps .pot .sxi .sti .sdd .key` | LibreOffice → `.pptx` → PPTX çıkarıcı |
| Çizim/yayın | `.vsd .vdx .pub .sxd .std .sda .cdr .wpg .cgm` | LibreOffice → `.pdf` → PDF çıkarıcı (gerekirse OCR) |
| PyMuPDF yerel | `.xps .oxps .epub .mobi .fb2 .cbz .svg` | PyMuPDF sayfaları (metin, yoksa OCR) |
| Eski görüntü | `.pcx .tga .dib .jpe .jfif .jp2 .j2k .jpx .psd .pnm .pbm .pgm .ppm` | Pillow/PyMuPDF → OCR |
| DjVu | `.djvu .djv` | `djvutxt` metin katmanı; boşsa `ddjvu -format=tiff` → OCR (DjVuLibre: paket `araclar\djvulibre\` ya da `%ProgramFiles(x86)%\DjVuLibre`) |
| Web arşivi | `.mht .mhtml` | `email` MIME → HTML çıkarıcı |
| Yardım | `.chm` | 7-Zip (`araclar\7z\7z.exe` ya da `%ProgramFiles%\7-Zip\7z.exe`) ile aç → HTML'ler bölüm |
| Eski düz metin | `.txt .asc .nfo .lst .prn .dos` | kodlama: BOM → UTF-8 → cp1254 / ISO-8859-9 / **cp857 (DOS Türkçe)** puanlaması (Türkçe harf sıklığı en yüksek olan; kutu çizme karakterleri cp857 lehine) |

- Dönüştürülen modern kopya depoda `<sha>\duzenlenebilir.<docx|xlsx|pptx|pdf>` olarak SAKLANIR (`belge.json`'a
  `"duzenlenebilir": "duzenlenebilir.docx"`). Veri Dedektifi bunu ArtCraft ile açma seçeneği olarak sunar.
- Dönüştürücü yoksa: `durum='desteklenmiyor'`, `hata` hangi aracın eksik olduğunu söyler (ör. "Eski WordPerfect: LibreOffice bulunamadı").
- Tür kuralı (5.7): bu tablodaki tüm uzantılar `belge` (görüntü satırı ve DjVu `goruntu`).

### 11.3 ArtCraft ile açma (Veri Dedektifi)
- ArtCraft (storytold/*, Apache-2.0) uygulamaları: WordCraft (`.docx .docm .dotx .dotm .doc .dot .rtf .odt .md .txt .html .tex`),
  GridCraft (`.xlsx .xlsm .xlsb .xls .ods .csv .tsv`), DeckCraft (`.pptx .potx .ppsx`), PdfCraft (`.pdf`),
  PhotoCraft (`.png .jpg .jpeg .tif .tiff .bmp .gif .webp .tga .psd .ico .avif`), FigCraft (`.svg`), CADCraft (`.dwg .dxf`).
  Açma komutu: `<uygulama>.exe "<dosya>"` (kayıt defterindeki `shell\open\command` ile aynı; ölçüldü).
- Bulma sırası (Çekirdek `ArtCraftKatalogu`): (1) Veri Dedektifi klasörü altında `artcraft\<uygulama>\**\<uygulama>.exe`
  (paketlenen taşınabilir sürüm), (2) `%ProgramFiles%\<Uygulama>\<uygulama>.exe`, (3) `%LOCALAPPDATA%\Programs\<Uygulama>\`.
  Aynı uygulamanın birden çok kopyası varsa **en yüksek sürüm** (FileVersionInfo.ProductVersion) seçilir.
- Arayüz: Dosyalar ve Belgeler sonuçlarının sağ tık menüsüne `Düzenle ▸ <Uygulama sürüm>` (uzantıya uyanlar) ve
  `Birlikte aç ▸ Windows varsayılanı`; dosyanın düzenlenebilir kopyası varsa `Düzenlenebilir kopyayı aç (<Uygulama>)` →
  kopya önce `%USERPROFILE%\Documents\Veri Dedektifi\<özgün ad>.<yeni uzantı>`'ya kopyalanır (arşivdeki özgün değişmez).
  Belge görüntüleyicinin üst çubuğunda `Düzenle (<Uygulama>)` düğmesi.
- Uygun uygulama yoksa menü maddesi gri ve ipucu: "ArtCraft uygulaması bulunamadı".

### 11.4 Paketleme
- `hazirla.ps1` ve `paketle-portal.ps1`: ArtCraft uygulamalarının **en güncel** sürümü paketleme anında GitHub API ile
  bulunur (`https://api.github.com/repos/storytold/<ad>/releases/latest`), `<ad>-<sürüm>-windows-x64-portable.zip`
  indirilir, GitHub'ın `digest` (sha256) değeriyle doğrulanır, `artcraft\<ad>\` altına açılır. Varsayılan liste:
  `wordcraft gridcraft deckcraft pdfcraft photocraft` (parametre `-ArtCraft`). Sürümler SURUMLER/OKU-BENI'ye yazılır.
  İndirme başarısızsa uyarı yazılır, paketleme sürer (ArtCraft isteğe bağlıdır).

### 11.5 Simgeler
- Gemini her iki uygulama için adına göre SVG simge tasarlar: Veri Fırını (veri + fırın), Veri Dedektifi (büyüteç + belge/film).
  256×256 görünüm kutusu, 16 piksele küçülünce de okunur, düz renk + en çok 3 renk, metin yok.
  SVG → PNG (PyMuPDF) → çok boyutlu `.ico` (16, 24, 32, 48, 64, 128, 256; Pillow). Dosyalar: `kaynak/varliklar/veri-firini.svg`,
  `veri-dedektifi.svg`; portal `Varliklar\uygulama.ico` + `logo-256.png`, menü `baslat\logo-256.png` bunlarla değişir.
