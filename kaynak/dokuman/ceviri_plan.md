# Çeviri — uygulama planı

> Yürütme: ZORUNLU kodlama hattı (K3). Yazar Gemini Flash (Codex/Copilot tükenmiş, Grok kapalı), inceleyici Sonnet alt ajanı + Argon. Adımlar `- [ ]` ile izlenir.

**Amaç:** en/az/ru konuşma dökümünü ve belge sayfasını Sarnıç Atölye'de NLLB ile Türkçeye çevirmek; Sarnıç'ta "Özgün / Türkçe" geçişi ve çeviride arama.
**Mimari:** Python `sarnicatolye/ceviri.py` tek çeviri yüzeyidir. Döküm JSON'u segment başına `ceviri`, `belge.json` sayfa başına `dil` + `ceviri` taşır. Dizin `sayfa.dil`, `sayfa.ceviri` sütunları ve ayrı `sayfa_ceviri_fts` tablosu alır. Sarnıç yalnız okur.
**Teknoloji:** CTranslate2 4.8.2, sentencepiece, NLLB-200 distilled 1.3B int8; .NET 8 WPF, Microsoft.Data.Sqlite.
**Tasarım:** `kaynak/dokuman/ceviri_tasarim.md`.

## Genel kısıtlar
- Diller: `en`→`eng_Latn`, `az`→`azj_Latn`, `ru`→`rus_Cyrl`; hedef `tur_Latn`. Kaynak belirteçleri `[kod] + sp + ["</s>"]`, `target_prefix=[["tur_Latn"]]`, çıktının ilk belirteci atılır.
- Çözme: `beam_size=4`, `repetition_penalty=1.0`, `no_repeat_ngram_size=0`, `max_decoding_length = min(512, int(2.0 × kaynak belirteç + 16))`. Çıktı cümle sayısına KESİLMEZ (Argon: kesme, NLLB'de bir cümleyi iki cümleye çeviren doğru çıktıyı siliyordu; NLLB ölçümü kesmesizdi).
- `dil == "tr"` olan döküm ve sayfa ÇEVRİLMEZ.
- Model yoksa ya da yüklenemezse işleme durmaz; `uyarilar`'a / günlüğe `Çeviri yapılamadı: <sebep>`.
- Mevcut belgeler yeniden OCR'lanmaz: `belge/hat.py` `SURUM` DEĞİŞMEZ.
- Dizin şema `surum` `'1'` kalır; değişiklik yalnız eklemedir (eski Sarnıç yeni dizini okur).
- Türkçe uyarı metni (aynen): `Makine çevirisi. Hata içerebilir. Kesin bilgi için özgün metne bakın.`
- Etiket metni (aynen): `çeviriden eşleşti`.
- Teknik adlar ASCII; düzyazı STE-TR.

## İnceleme odağı (testi aşağıdaki görevlerde)
1. Uzun sayfa (> 512 belirteç tek cümle, ör. noktasız OCR bloğu) → kesilmeden parçalara bölünür, çökme yok (Görev 1).
2. Karışık dilli sayfa (Türkçe metin içinde İngilizce alıntı) → `tr` sayılır, çevrilmez (Görev 1).
3. Boş/boşluk segment ve sayfa → çevrilmez, `ceviri` alanı yazılmaz (Görev 1, 2).
4. Eski dizin (`dil`/`ceviri` sütunu yok, `sayfa_ceviri_fts` yok) → Sarnıç hata vermez, çeviri araması yalnız atlanır (Görev 5).
5. Eski döküm JSON'u (`ceviri` alanı yok) → geçiş düğmesi görünmez (Görev 5, 6).

---

### Görev 1: Çeviri çekirdeği
**Dosyalar:** Yeni `kaynak/app/sarnicatolye/ceviri.py`; değişen `ayar.py` (`CeviriAyar`), `kaynak/ayar.toml` (`[ceviri]`), `yollar.py`, `hatalar.py`; test `kaynak/tests/test_ceviri.py`.
**Üretir:**
- `cumlelere_bol(metin: str) -> list[str]` — `[.!?…]+\s+` ve boş satır (`\n\s*\n`) böler; tek `\n` BÖLMEZ (OCR satırları cümle ortasında kırılır), boşluğa çevrilir. Noktadan önceki sözcük Romen rakamı, sayı ya da kapalı kısaltma listesindeyse bölmez: `Dr Prof Mr Mrs Ms No vs vb bkz örn St Jr г т др им` (büyük/küçük harf duyarsız). 400 karakteri aşan parça son boşlukta, boşluk yoksa 400 karakterlik sabit dilimle bölünür.
- `dil_tahmin(metin: str) -> str` — sırayla: harf < 5 → `"tr"`; Kiril harf oranı ≥ 0.5 → `"ru"`; `ə/Ə` var ya da (harf ≥ 20 ve (`q`+`x`) oranı ≥ 0.01) → `"az"`; sonra durak sözcüğü sayımı (sözcük sınırında, küçük harf): tr `ve bir bu için ile olarak da de çok daha gibi olan` / en `the and of to is in that for with as are on` / az `və bir bu üçün ilə olan edir deyil kimi daha çox` — en yüksek sayı kazanır; eşitlikte ya da hepsi 0 ise Türkçe harf (ğ ı ş İ ç ö ü) oranı ≥ 0.02 → `"tr"`, değilse `"en"`. Gerekçe (Argon, doğrulandı `belge/ocr.py:284`): OCR Türkçe sözlüğü Kiril dışı her satıra uygulanır ve İngilizce/Azerice metne Türkçe harf ekleyebilir; yalnız harf oranı yanıltır.
- `class CeviriHatasi(SarnicAtolyeHatasi)`.
- `class Cevirmen` — `__init__(self, model_yolu: Path, cihaz: str, compute_type: str, cevirici=None)`; `cevir(self, metinler: list[str], kaynak_dil: str) -> list[str]`; model yoksa `CeviriHatasi`. `cevirici` test için sahte CTranslate2 nesnesi. Önbellek: süreç içi `dict[(dil, sha256(metin))] -> str`.
- `ayar.CeviriAyar(etkin: bool = True, model: str = "", diller: tuple = ("en","az","ru"), batch: int = 16)`; boş `model` → `yollar.ceviri_modeli()` = `<paket kök>/modeller/nllb-200-distilled-1.3B-ct2-int8`.
- [ ] Testler: `test_cumle_sira_sayisi` ("I. Dünya Savaşı başladı. Sonra bitti." → 2 cümle); `test_kisa_sozcuk_sonu` ("Bugün iş. Yarın ay." → 2 cümle); `test_tek_satir_sonu_bolmez` ("Toplantı\nyarın başlar." → 1 cümle); `test_bosluksuz_uzun` (900 karakter boşluksuz → 3 parça); `test_dil_tahmin`: ru/az/tr/en birer örnek; "Bu raporda 'meeting minutes' geçiyor ve bu çok önemli." → tr; ə'siz Azerice "Biz bu qerari xalq ucun verdik ve bu qanun olan bir xidmetdir" → az; Türkçe harf eklenmiş İngilizce "The meeting ıs on Friday and the report ış ready for the team" → en; "Q3 Report" → en (q/x kuralı 20 harfin altında çalışmaz); "OK" → tr (harf < 5); `test_bos_metin_cevrilmez` (`["", "  "]` → `["", "  "]`, sahte çevirici çağrılmaz); `test_uzun_parca` (600 karakter noktasız → sahte çeviriciye giden her parça ≤ 400 karakter); `test_model_yok` (`CeviriHatasi`); `test_tr_cevrilmez` (`kaynak_dil="tr"` → girdi aynen).
- [ ] Koş: paket Python ile `unittest` (`dist/SarnicAtolye-GPU/python/python.exe`, sys.path eklemesiyle; özet dosyası «Komutlar»).
- [ ] Commit.

### Görev 2: Döküm çevirisi
**Dosyalar:** `cikti.py` (`json_meta`), `hat.py` satır ~213, `grup.py` (`_varsayilan_transkript` ~329 ve `json_meta` çağrısı ~612: çok açılı derste segmentin `ceviri`'si ve `dil` korunur; Argon bulgusu); test `kaynak/tests/test_ceviri_dokum.py`.
**Tüketir:** Görev 1 `Cevirmen`, `CeviriHatasi`.
**Üretir:** `json_meta(..., ceviriler: list[str] | None = None, ceviri_model: str | None = None)`; segmente `"ceviri"` (yalnız boş olmayan çeviride), köke `"ceviri_dil": "tr"`, `"ceviri_model"`. `.tr.srt` YAZILMAZ (Sarnıç altyazıyı `SrtUretici` ile geçerli görünümden üretir; Argon bulgusu). `kaynak_ozet` YALNIZ özgün metinden hesaplanır (değişmez).
- [ ] Testler: `ceviriler` verilince JSON'da `ceviri` ve kök alanları; verilmeyince JSON eskisiyle bayt bayt aynı; `kaynak_ozet` iki durumda eşit; `dil="tr"` iken hat çeviriciyi çağırmaz (sahte); çok açılı ders JSON'unda `ceviri` ve `dil` korunur; çevirici `CeviriHatasi` atınca işleme `basarili=True`, günlükte uyarı.
- [ ] Koş, commit.

### Görev 3: Belge çevirisi
**Dosyalar:** `belge/hat.py`; `belge/onbellek.py` (`json_yaz` geçici adı `f"{yol.name}.{os.getpid()}.tmp"`; eşzamanlı yazarlar çakışmasın); yeni işlev `belge/ceviri_ekle.py`; test `kaynak/tests/test_belge_ceviri.py`.
**Üretir:** `ceviri_ekle(veri: dict, cevirmen: Cevirmen | None) -> bool` — her sayfaya `dil` yazar; `dil != "tr"` ise `ceviri`; köke `"ceviri_surum": 1`. Değişiklik olduysa True. `isle_belge` yeni sonuçta ve önbellekten dönen eski `belge.json`'da (`ceviri_surum` yoksa) çağırır, değiştiyse `json_yaz` ile yeniden yazar. `cevirmen is None` → yalnız `dil` yazılır, `ceviri_surum` YAZILMAZ (model gelince tekrar denenir).
- [ ] Testler: Rusça sayfa → `dil=ru`, `ceviri` dolu; Türkçe sayfa → `ceviri` yok; önbellekteki eski `belge.json` OCR'sız güncellenir (`cikar` sahtesi çağrılmaz); `cevirmen=None` → `ceviri_surum` yok.
- [ ] Koş, commit.

### Görev 4: Dizin
**Dosyalar:** `kaynak/ortak/dizin_sema.sql`, `dizin/vt.py`, `dizin/dizinle.py`; test `kaynak/tests/test_dizin_ceviri.py`.
**Üretir:**
- Şema: `sayfa` tablosuna `dil TEXT`, `ceviri TEXT` (yeni dizin); `CREATE VIRTUAL TABLE IF NOT EXISTS sayfa_ceviri_fts USING fts5(norm, content='', contentless_delete=1, tokenize='unicode61 remove_diacritics 0', prefix='2 3')`; `sayfa_ad` tetikleyicisi iki FTS'den de siler. `vt.ac` göçünde `DROP TRIGGER IF EXISTS sayfa_ad` + yeniden oluştur (mevcut dizinde `CREATE TRIGGER IF NOT EXISTS` eski tetikleyiciyi güncellemez).
- `vt.ac`: `PRAGMA table_info(sayfa)` sütun yoksa `ALTER TABLE sayfa ADD COLUMN ...`.
- `sayfalari_yaz`: `dil`, `ceviri` yazar; `ceviri` doluysa `sayfa_ceviri_fts`'ye `icerik_normalle(ceviri)`.
- `dizinle.calistir`: ana döngüden sonra çeviri geçişi — `durum='tamam'` ve `sha` dolu, sayfalarından biri `dil IS NULL` olan dosyalar için `belge.json` okunur, `ceviri_ekle` (değişmese de) ve `sayfalari_yaz` HER durumda çalışır (belge.json yazılıp commit'ten önce kesilen süreç kurtulur). Dosya başına commit + `ilerleme()` (ara yayın sürsün).
- [ ] Testler: eski şemalı dizin açılınca sütunlar eklenir; "toplanti" sorgusu `sayfa_ceviri_fts`'de Rusça sayfayı bulur; sayfa silinince iki FTS'den de silinir; çeviri geçişi zaten işlenmiş belgeyi yeniden OCR'lamaz.
- [ ] Koş, commit.

### Görev 5: Sarnıç çekirdeği (C#)
**Dosyalar:** `Portal.Cekirdek/Segment.cs`, `DuzeltmeDeposu.cs` (`EfektifSegment`'e `string? Ceviri`; `Birlestir` taşır — oynatıcı ve video araması `Transkript.Segmentler`'i değil bunu kullanır), `TranskriptOkuyucu.cs`, `IndeksOnbellek.cs` (önbellek sürümü +1, `Ceviri` alanı), `MetinAramaMotoru.cs`, `AramaSonucu.cs`, `Dizin/SayfaDeposu.cs`, `Dizin/IcerikAramaMotoru.cs`, `Dizin/DosyaAramaMotoru.cs:164`; testler `Portal.Testleri/CeviriTestleri.cs`.
**Üretir:**
- `record Segment(int No, double Bas, double Son, string Metin, string? Ceviri = null)`; `Transkript.CeviriVar => Segmentler.Any(s => !string.IsNullOrWhiteSpace(s.Ceviri))`.
- `SayfaKaydi(..., string Metin, string? Dil = null, string? Ceviri = null)`; sütun yoksa null.
- `AramaSonucu` ve `IcerikSonucu`'na `bool CevirideEslesti = false`. Özgün metinde eşleşme yoksa ve çeviride varsa true.
- `IcerikAramaMotoru`/`DosyaAramaMotoru`: `sayfa_ceviri_fts` varsa (`sqlite_master` kontrolü, bağlantı başına bir kez) birleşik arama; CTE'de `rowid` başına tekilleştir, özgün eşleşme öncelikli. `CevirideEslesti` ise alıntı (`MetinEsleme.AlintiUret`) `ceviri` üzerinden üretilir. Video aramasında da alıntı çeviriden.
- [ ] Testler: `ceviri` alanlı JSON okunur; alansız JSON `CeviriVar=false`; eski şemalı SQLite dosyası (sütunsuz, tablosuz) ile `SayfaDeposu.Getir` ve `IcerikAramaMotoru.Ara` hata vermez; Türkçe sorgu yalnız çeviride eşleşen sayfayı `CevirideEslesti=true` ile döndürür; video aramasında aynı.
- [ ] `dotnet test` (536 + yeni) geçer, commit.

### Görev 6: Sarnıç arayüzü
**Dosyalar:** `OynaticiPenceresi.xaml(.cs)`, `CokAciliOynaticiPenceresi.xaml(.cs)`, `BelgePenceresi.xaml(.cs)`, arama sonuç şablonu (`AramaPenceresi.xaml`), `KokYapilandirma.cs` (kullanıcı ayarı `CeviriGoster: bool`, varsayılan false).
- Döküm panelinde ve belge sayfa metninde "Özgün / Türkçe" geçiş düğmesi (mevcut düğme stiliyle). Yalnız `CeviriVar` / sayfada `Ceviri` varken görünür.
- Türkçe görünümde metnin üstünde sabit uyarı (Genel kısıtlardaki metin).
- Tam ekran altyazı (`SrtUretici`) geçerli görünümün metnini kullanır.
- Arama sonucunda `CevirideEslesti` ise küçük `çeviriden eşleşti` etiketi.
- Düğme durumu `kullanici.json`'a yazılır: `AyarVerisi`'ne `[JsonPropertyName("ceviri_goster")] bool CeviriGoster`; `Kaydet` var olan alanları KORUR (bugün yalnız `kok` yazıyor, Argon bulgusu `KokYapilandirma.cs:268`).
- Çeviri eşleşmesiyle açılan pencere Türkçe görünümde açılır; pencere içi arama (`BelgePenceresi` norm önbelleği, `OynaticiYardimcisi.SegmentFiltrele`) geçerli görünümün metninde çalışır.
- [ ] Derleme 0 uyarı; görsel denetim Claude'da (ekran görüntüsü).
- [ ] Commit.

### Görev 7: Paket, belge ve ölçüm testi
**Dosyalar:** `hazirla.ps1` (`sentencepiece` tekerleği GPU+CPU listesine ve satır 188/226 `Test-Path` varlık kontrolüne; model klasörünü `_indir/modeller-ceviri`'den pakete `modeller\` altına kopyala), `paketle.ps1` (model klasörü dahil), `kaynak/baslat/OKU-BENI.txt` (lisans satırı: `NLLB-200 (Meta), CC-BY-NC 4.0 — yalnız ticari olmayan kullanım`), `bulgu_spec.md` (§11.6 Çeviri, ölçümler), yeni `kaynak/tests/test_ceviri_olcum.py` (24 cümle; model yoksa `skipTest`; chrF++ en ≥ 80, az ≥ 65, ru ≥ 65; GPU yoksa CPU süresini yazdırır).
- [ ] Ölçüm testini GPU ve CPU'da koş; CPU süresini `bulgu_spec.md`'ye yaz.
- [ ] Commit.

## Komutlar
- Python: `dist/SarnicAtolye-GPU/python/python.exe -c "import sys; sys.path[:0]=['kaynak/app','kaynak/tests','<pylib>']; import unittest; unittest.main(module=None, argv=['x','discover','-s','kaynak/tests','-p','test_ceviri*.py'])"` (CUDA için `dist/SarnicAtolye-GPU/cuda` PATH'te).
- C#: `dotnet test kaynak/portal/Portal.Testleri -c Release`.
