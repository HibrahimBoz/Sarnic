# Çeviri tasarımı — Türkçe dışı içeriği Türkçeye çevirme (taslak, onay bekliyor)

Tarih: 2026-10-31. Durum: kullanıcı onayladı (2026-10-31).

## 1. Amaç
- Kullanıcı ve ekibi en, az ve ru dilindeki konuşma dökümünü ve belge metnini Türkçe okur.
- Çeviri tamamen kapalı ağda çalışır. İnternet bağlantısı gerekmez.
- Ürün ticari değildir (kullanıcı kararı 2026-10-31). Ticari karar alınırsa model seçimi yeniden değerlendirilir.

## 2. Kararlar (kullanıcı verdi)
| Konu | Karar |
|---|---|
| Nerede çevrilir | Seçenek 3: Sarnıç Atölye işleme sırasında önceden çevirir. Sarnıç yalnız gösterir. |
| Gösterim | Sarnıç'ta "Özgün / Türkçe" düğmesi iki metin arasında geçiş yapar. |
| Model | NLLB-200 distilled 1.3B, CTranslate2 int8 (lisans CC-BY-NC: yalnız ticari olmayan kullanım). |
| Simge | Ayrı iş, tamamlandı. |

## 3. Yaklaşımlar (ölçüldü 2026-10-31, 8 cümle × 3 dil, GPU int8, referansları Claude yazdı; örneklem küçük)
| Seçenek | en→tr chrF++ | az→tr | ru→tr | Lisans | Sonuç |
|---|---|---|---|---|---|
| A. NLLB-200 1.3B (seçildi) | 83.8 | 70.1 | 70.9 | CC-BY-NC | Uydurma görülmedi. 8 cümle ~2,2 s. Disk 1,3 GB. |
| B. MADLAD-400 3B | 76.5 | 79.9 | 71.8 | Apache-2.0 | Tekrar ve uydurma yapıyor; "cümə" → "Perşembe". Disk 2,8 GB. Ticari olursa yedek. |
| C. TranslateGemma | — | — | — | Gemma | Ölçülmedi. Yeni çalışma ortamı gerekir. az desteği doğrulanmadı. |

## 4. Sarnıç Atölye tarafı (Python)
1. Yeni modül `videohat/ceviri.py`. Model bir kez yüklenir. Cihaz `cihaz.py` kuralıyla seçilir (GPU yoksa CPU).
2. Girdi metni cümlelere bölünür. Bölücü sıra sayısında ve kısaltmada ("I.", "12.", "Dr.") bölmez. Cümleler toplu (batch) çevrilir. Çıktı cümle sayısına kesilmez (NLLB ölçümü kesmesizdi).
3. Kaynak dil kodları: `en` → `eng_Latn`, `az` → `azj_Latn`, `ru` → `rus_Cyrl`. Hedef `tur_Latn`.
4. Ayar: `ayar.toml` içinde yeni `[ceviri]` bölümü: `etkin = true`, `model = "<yol>"`, `diller = ["en", "az", "ru"]`.
5. Konuşma dökümü: `stt` dili `tr` değilse her segment çevrilir. Çıktı JSON'unda her segmente `ceviri` alanı eklenir. Kök alanlar: `ceviri_dil = "tr"`, `ceviri_model = "<model adı>"`.
6. Belge: her sayfanın dili belirlenir. Kural: Kiril harf oranı, Azerice harfler (`ə`, `q`, `x`) ve durak sözcüğü sayımı (ayrıntı `ceviri_plan.md` Görev 1). Dil `tr` değilse sayfa çevrilir. `belge.json` sayfalarına `dil` ve `ceviri` alanları eklenir.
7. Önbellek: çeviri metnin SHA256 özetine bağlanır. Aynı metin yeniden çevrilmez.
8. Hata: model yoksa ya da yüklenemezse işleme durmaz. `uyarilar` listesine "Çeviri yapılamadı: <sebep>" yazılır.

## 5. Dizin (ortak şema)
- `sayfa` tablosuna `dil TEXT` ve `ceviri TEXT` sütunları eklenir. Şema sürümü artar; eski dizin okunmaya devam eder.
- Türkçe çeviri aramaya girer (kullanıcı kararı 2026-10-31). "toplantı" sorgusu Rusça "встреча" geçen sayfayı da bulur. Sonuçta "çeviriden eşleşti" etiketi gösterilir.

## 6. Sarnıç tarafı (C#)
1. Oynatıcı penceresi: döküm panelinde "Özgün / Türkçe" düğmesi. Düğme yalnız `ceviri` alanı olan dökümde görünür.
2. Belge penceresi: sayfa metninde aynı düğme. Düğme yalnız `ceviri` alanı olan sayfada görünür.
3. Türkçe görünümde metnin üstünde sabit uyarı: "Makine çevirisi. Hata içerebilir. Kesin bilgi için özgün metne bakın."
4. Altyazı: tam ekran altyazısı geçerli görünümün metninden üretilir (Türkçe görünümde çeviri).
5. Düğmenin son durumu `kullanici.json` dosyasında saklanır.

## 7. Paketleme
- Sarnıç Atölye paketine `sentencepiece` tekerleği (wheel) ve model klasörü eklenir: `modeller\nllb-200-distilled-1.3B-ct2-int8` (1,3 GB, ölçüldü).
- `ctranslate2` pakette zaten var (4.8.2).
- OKU-BENI dosyasına model lisansı (CC-BY-NC 4.0, Meta) ve atıf satırı eklenir.
- Sarnıç paketi değişmez. Sarnıç yalnız JSON'daki çeviriyi okur; model taşımaz.

## 8. Kabul ölçütü
1. 24 cümlelik ölçüm seti `kaynak/tests` altına girer. chrF++ en ≥ 80, az ≥ 65, ru ≥ 65 (GPU). Eşik ölçülen değerin ~5 puan altındadır.
2. Çeviri uzunluğu sınırlıdır: `max_decoding_length = min(512, 2 × kaynak belirteç + 16)`.
3. Türkçe döküm çevrilmez. Türkçe sayfa çevrilmez.
4. Model yokken işleme hatasız biter ve uyarı yazar.
5. CPU süresi ölçülür ve belgeye yazılır (henüz ölçülmedi).
6. C#: düğme görünürlüğü, uyarı metni ve `.tr.srt` seçimi için birim testleri.

## 9. Kapsam dışı
- Türkçeden başka dile çeviri.
- Sarnıç içinde canlı (anlık) çeviri.
- Kullanıcının çeviriyi elle düzeltmesi.

## 10. Kodlama hattı
- Kademe K3 (yeni modül + şema + iki uygulama).
- Codex limiti 4 Kasım 11:40'a kadar dolu. Copilot 1 Kasım'a kadar dolu. Grok kullanıcı kararıyla dışarıda.
- Öneri: tasarım kararlarını Argon'a doğrulatmak; yazımı Gemini Flash (Python ve C# ayrı iki iş); incelemeyi Argon ve bir Sonnet alt ajanı.
