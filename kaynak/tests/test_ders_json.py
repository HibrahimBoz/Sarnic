"""Video Transkriptör - ders.json ve isle-grup birim testleri (≥ 20 test)."""
from __future__ import annotations

import json
from pathlib import Path
import shutil
import sys
import tempfile
import unittest

import numpy as np

# Modül arama yolunu ekle
kok_dizin = Path(__file__).resolve().parent.parent
app_dizin = kok_dizin / "app"
for p in (str(kok_dizin), str(app_dizin)):
    if p not in sys.path:
        sys.path.insert(0, p)

from videohat import cikti, ders_json, grup
from videohat.stt import Segment


def _sentetik_konusma(sure_sn: float, sr: int = 8000, tohum: int = 42) -> np.ndarray:
    """Konuşma benzeri genlik zarflı sentetik sinyal."""
    rng = np.random.default_rng(tohum)
    n = int(round(sure_sn * sr))
    if n <= 0:
        return np.zeros(0, dtype=np.float32)

    gurultu = rng.standard_normal(n)
    G = np.fft.rfft(gurultu)
    freqs = np.fft.rfftfreq(n, d=1.0 / sr)
    mask = (freqs >= 300.0) & (freqs <= 3400.0)
    bantli = np.fft.irfft(G * mask, n=n)

    t = np.arange(n) / sr
    ana_zarf = np.maximum(
        0.0,
        np.sin(2.0 * np.pi * 1.5 * t) + 0.6 * np.sin(2.0 * np.pi * 3.5 * t) - 0.2,
    )
    sinyal = bantli * ana_zarf
    rms = np.sqrt(np.mean(sinyal**2))
    if rms > 1e-9:
        sinyal = sinyal / rms * 0.2
    return sinyal.astype(np.float32)


class TestDersJsonVeGrup(unittest.TestCase):
    """ders.json ve grup orkestrasyonu birim testleri."""

    def setUp(self):
        self.gecici_dizin = tempfile.mkdtemp()
        self.klasor = Path(self.gecici_dizin)

    def tearDown(self):
        shutil.rmtree(self.gecici_dizin, ignore_errors=True)

    # 1. Kanal Bulma (Alt Klasör)
    def test_01_kanal_bulma_alt_klasorler(self):
        """Alt klasörler kanal adı olarak doğru tespit edilir."""
        (self.klasor / "cam1").mkdir()
        (self.klasor / "cam2").mkdir()
        (self.klasor / "cam1" / "001.mp4").touch()
        (self.klasor / "cam1" / "002.mp4").touch()
        (self.klasor / "cam2" / "001.mp4").touch()

        kanallar = grup.kanallari_bul(self.klasor)
        self.assertEqual(list(kanallar.keys()), ["cam1", "cam2"])
        self.assertEqual(len(kanallar["cam1"]), 2)
        self.assertEqual(len(kanallar["cam2"]), 1)

    # 2. Kanal Bulma (Regex Kalıbı - Düz Klasör)
    def test_02_kanal_bulma_kalip_duz_klasor(self):
        """Düz klasörde dosya adı kalıbıyla kanal adları çıkarılır."""
        (self.klasor / "kanal1_001.mp4").touch()
        (self.klasor / "kanal1_002.mp4").touch()
        (self.klasor / "kanal2_001.mp4").touch()

        kanallar = grup.kanallari_bul(self.klasor)
        self.assertIn("kanal1", kanallar)
        self.assertIn("kanal2", kanallar)
        self.assertEqual(len(kanallar["kanal1"]), 2)
        self.assertEqual(len(kanallar["kanal2"]), 1)

    # 3. Kanal Bulma (Tire ve Farklı Uzantılar)
    def test_03_kanal_bulma_farkli_ayrac_ve_uzanti(self):
        """Tireli ve farklı uzantılı (mkv, mov, avi) dosyalarda kanal tespiti."""
        (self.klasor / "kameraA-01.mkv").touch()
        (self.klasor / "kameraA-02.avi").touch()
        (self.klasor / "kameraB_01.mov").touch()

        kanallar = grup.kanallari_bul(self.klasor)
        self.assertEqual(sorted(kanallar.keys()), ["kameraA", "kameraB"])
        self.assertEqual(len(kanallar["kameraA"]), 2)
        self.assertEqual(len(kanallar["kameraB"]), 1)

    # 4. Doğal Sıra (2 < 10)
    def test_04_dogal_sira_dosyalar(self):
        """Dosyalar doğal sırayla (2 < 10) sıralanır."""
        (self.klasor / "cam1").mkdir()
        (self.klasor / "cam1" / "parca_10.mp4").touch()
        (self.klasor / "cam1" / "parca_2.mp4").touch()
        (self.klasor / "cam1" / "parca_1.mp4").touch()

        kanallar = grup.kanallari_bul(self.klasor)
        isimler = [f.name for f in kanallar["cam1"]]
        self.assertEqual(isimler, ["parca_1.mp4", "parca_2.mp4", "parca_10.mp4"])

    # 5. Doğal Sıra (Kanal Adları)
    def test_05_dogal_sira_kanal_adlari(self):
        """Kanal adları doğal sırayla sıralanır (cam2 < cam10)."""
        (self.klasor / "cam10").mkdir()
        (self.klasor / "cam2").mkdir()
        (self.klasor / "cam1").mkdir()
        (self.klasor / "cam10" / "01.mp4").touch()
        (self.klasor / "cam2" / "01.mp4").touch()
        (self.klasor / "cam1" / "01.mp4").touch()

        kanallar = grup.kanallari_bul(self.klasor)
        self.assertEqual(list(kanallar.keys()), ["cam1", "cam2", "cam10"])

    # 6. Çizelge Entegrasyonu
    def test_06_cizelge_entegrasyonu(self):
        """hazirla_kanal enjekte edilen prob_fn ile doğru çizelge üretir."""
        dosyalar = [Path("cam1/001.mp4"), Path("cam1/002.mp4")]
        sahte_bilgi = {
            str(dosyalar[0]): (1000.0, 10.0),
            str(dosyalar[1]): (1010.0, 15.0),
        }

        def sahte_prob(d: str):
            return sahte_bilgi[str(d)]

        cizelge = grup.hazirla_kanal(dosyalar, prob_fn=sahte_prob)
        self.assertEqual(len(cizelge.dilimler), 2)
        self.assertAlmostEqual(cizelge.toplam_sure, 25.0)
        self.assertAlmostEqual(cizelge.dilimler[0].sanal_bas, 0.0)
        self.assertAlmostEqual(cizelge.dilimler[1].sanal_bas, 10.0)

    # 7. Çizelge Boşluk Yönetimi
    def test_07_hazirla_kanal_bosluklu_parcalar(self):
        """Zaman damgaları arasında boşluk varsa boşluk dilimi eklenir."""
        dosyalar = [Path("cam1/001.mp4"), Path("cam1/002.mp4")]
        sahte_bilgi = {
            str(dosyalar[0]): (1000.0, 10.0),
            str(dosyalar[1]): (1015.0, 10.0),  # 5 sn boşluk
        }

        cizelge = grup.hazirla_kanal(dosyalar, prob_fn=lambda d: sahte_bilgi[str(d)])
        self.assertEqual(len(cizelge.dilimler), 3)  # kayit, bosluk, kayit
        self.assertEqual(cizelge.dilimler[1].tur, "bosluk")
        self.assertAlmostEqual(cizelge.dilimler[1].sure, 5.0)
        self.assertAlmostEqual(cizelge.toplam_sure, 25.0)

    # 8. Referans Seçimi (En Yüksek Enerji)
    def test_08_referans_sec_otomatik(self):
        """En yüksek enerji/skor skoruna sahip kanal referans seçilir."""
        kanallar = {"cam1": None, "cam2": None, "cam3": None}
        skorlar = {"cam1": 12.0, "cam2": 45.5, "cam3": 5.0}

        ref = grup.referans_sec(kanallar, ses_enerji_fn=lambda k: skorlar[k])
        self.assertEqual(ref, "cam2")

    # 9. Referans Seçimi (Manuel Override)
    def test_09_referans_sec_override(self):
        """Manuel override parametresi enerjiden bağımsız olarak seçilir."""
        kanallar = {"cam1": None, "cam2": None}
        skorlar = {"cam1": 100.0, "cam2": 10.0}

        ref = grup.referans_sec(
            kanallar,
            ses_enerji_fn=lambda k: skorlar[k],
            manuel_override="cam2",
        )
        self.assertEqual(ref, "cam2")

    # 10. Referans Seçimi (Boş Kanal Hatası)
    def test_10_referans_sec_bos_kanal_hatasi(self):
        """Kanal listesi boş olduğunda ValueError fırlatılır."""
        with self.assertRaises(ValueError):
            grup.referans_sec([])

    # 11. Senkron Entegrasyonu (Sentetik Sinyaller, Bilinen Ofset)
    def test_11_senkron_entegrasyonu_sentetik_sinyaller(self):
        """Sentetik sinyallerle 3 kanal: bilinen ofset bulunur, ilişkisiz kanal guvenilir=False olur."""
        sr = 8000
        ref_ses = _sentetik_konusma(12.0, sr=sr, tohum=42)

        # cam2: ref'e göre 0.10 sn gecikmeli
        ofset_ornek = int(round(0.10 * sr))
        cam2_ses = np.zeros_like(ref_ses)
        cam2_ses[ofset_ornek:] = ref_ses[:-ofset_ornek]

        # cam3: ilişkisiz beyaz gürültü
        rng = np.random.default_rng(999)
        cam3_ses = rng.standard_normal(len(ref_ses)).astype(np.float32) * 0.1

        kanal_sesleri = {"ref": ref_ses, "cam2": cam2_ses, "cam3": cam3_ses}
        sonuclar = grup.senkronla(ref_ses, kanal_sesleri, sr=sr)

        self.assertTrue(sonuclar["ref"].guvenilir)
        self.assertAlmostEqual(sonuclar["ref"].ofset_sn, 0.0, places=3)

        self.assertTrue(sonuclar["cam2"].guvenilir)
        self.assertAlmostEqual(sonuclar["cam2"].ofset_sn, 0.10, places=2)

        # İlişkisiz kanal güvenilir olmamalı
        self.assertFalse(sonuclar["cam3"].guvenilir)

    # 12. Tek Kanal Senkron Atlanır
    def test_12_tek_kanal_senkron_atlanir(self):
        """Tek kanallı derste senkron işlemi atlanır ve tek_kanal yöntemi atanır."""
        (self.klasor / "tek_cam").mkdir()
        (self.klasor / "tek_cam" / "01.mp4").touch()

        def sahte_prob(d):
            return 0.0, 10.0

        def sahte_ses(d, sr=8000):
            return np.ones(10 * sr, dtype=np.float32)

        def sahte_transkript(ses):
            return [Segment(0.0, 5.0, "Tek kanal")]

        sonuc = grup.isle_grup(
            self.klasor,
            prob_fn=sahte_prob,
            ses_yukle_fn=sahte_ses,
            transkript_fn=sahte_transkript,
        )

        self.assertTrue(sonuc.basarili)
        self.assertIsNotNone(sonuc.ders_tanimi)
        self.assertEqual(sonuc.ders_tanimi.senkron.yontem, "tek_kanal")
        self.assertTrue(sonuc.ders_tanimi.senkron.guvenilir)

    # 13. ders.json Gidiş-Dönüş (Round-Trip)
    def test_13_ders_json_gidis_donus(self):
        """DersTanimi nesnesi dosyaya yazılıp okunduğunda tüm alanlar korunur."""
        parca = ders_json.ParcaKaydi(
            dosya="cam1/001.mp4",
            sanal_bas=0.0,
            sanal_son=60.0,
            yerel_bas=0.0,
            tur="kayit",
            ofset_sn=0.5,
            guven=0.95,
        )
        kanal = ders_json.Kanal(
            ad="cam1",
            parcalar=[parca],
            ofset_sn=0.5,
            drift_ppm=10.0,
            guven=0.95,
            guvenilir=True,
            neden="basarili",
        )
        senkron_b = ders_json.SenkronBilgisi(
            yontem="gcc_phat",
            drift_ppm=10.0,
            guven=0.95,
            guvenilir=True,
            neden="basarili",
        )
        ders = ders_json.DersTanimi(
            ad="matematik_101",
            kanallar=[kanal],
            referans_kanal="cam1",
            senkron=senkron_b,
            elle_isaretler=[{"zaman": 15.0, "not": "konu baslangici"}],
            sema_surumu=1,
        )

        hedef = self.klasor / "ders.json"
        ders_json.yaz(hedef, ders)
        okunan = ders_json.oku(hedef)

        self.assertEqual(okunan.ad, ders.ad)
        self.assertEqual(okunan.referans_kanal, ders.referans_kanal)
        self.assertEqual(okunan.sema_surumu, 1)
        self.assertEqual(len(okunan.kanallar), 1)
        self.assertEqual(okunan.kanallar[0].ad, "cam1")
        self.assertAlmostEqual(okunan.kanallar[0].ofset_sn, 0.5)
        self.assertAlmostEqual(okunan.kanallar[0].drift_ppm, 10.0)
        self.assertEqual(len(okunan.kanallar[0].parcalar), 1)
        self.assertEqual(okunan.kanallar[0].parcalar[0].dosya, "cam1/001.mp4")
        self.assertEqual(len(okunan.elle_isaretler), 1)

    # 14. ders.json Türkçe Karakter Korunur
    def test_14_ders_json_turkce_karakter_korunur(self):
        """Türkçe karakterler JSON dosyasında doğrudan UTF-8 olarak korunur."""
        turkce_ad = "Türkçe Dersi: Öğretmen İle Çocuklar (Şiir & Yağmur)"
        ders = ders_json.DersTanimi(
            ad=turkce_ad,
            kanallar=[
                ders_json.Kanal(
                    ad="ön_kamera",
                    parcalar=[
                        ders_json.ParcaKaydi(
                            dosya="ön/parça_01.mp4",
                            sanal_bas=0.0,
                            sanal_son=10.0,
                            yerel_bas=0.0,
                            tur="kayit",
                            ofset_sn=0.0,
                            guven=1.0,
                        )
                    ],
                )
            ],
            referans_kanal="ön_kamera",
            senkron=ders_json.SenkronBilgisi(
                yontem="gcc_phat",
                drift_ppm=0.0,
                guven=1.0,
                guvenilir=True,
                neden="başarılı",
            ),
        )

        hedef = self.klasor / "ders.json"
        ders_json.yaz(hedef, ders)

        ham_metin = hedef.read_text(encoding="utf-8")
        self.assertIn("Türkçe Dersi", ham_metin)
        self.assertIn("ön_kamera", ham_metin)
        self.assertIn("başarılı", ham_metin)
        self.assertNotIn(r"\u011f", ham_metin)  # ğ escape edilmemiş olmalı

    # 15. ders.json Şema Hatası (Eksik Kök Alan)
    def test_15_ders_json_sema_hatasi_eksik_kok_alan(self):
        """Kök sözlükte zorunlu alan eksik olduğunda ValueError fırlatılır."""
        gecersiz_veri = {
            "ad": "ders1",
            "kanallar": [],
            # referans_kanal eksik
            "senkron": {
                "yontem": "gcc_phat",
                "drift_ppm": 0.0,
                "guven": 1.0,
                "guvenilir": True,
                "neden": "ok",
            },
            "elle_isaretler": [],
            "sema_surumu": 1,
        }
        hedef = self.klasor / "bozuk.json"
        hedef.write_text(json.dumps(gecersiz_veri), encoding="utf-8")

        with self.assertRaises(ValueError):
            ders_json.oku(hedef)

    # 16. ders.json Şema Hatası (Eksik Kanal Alanı)
    def test_16_ders_json_sema_hatasi_eksik_kanal_alani(self):
        """Kanal içinde 'parcalar' eksikse ValueError fırlatılır."""
        gecersiz_veri = {
            "ad": "ders1",
            "referans_kanal": "cam1",
            "kanallar": [{"ad": "cam1"}],  # parcalar eksik
            "senkron": {
                "yontem": "gcc_phat",
                "drift_ppm": 0.0,
                "guven": 1.0,
                "guvenilir": True,
                "neden": "ok",
            },
            "elle_isaretler": [],
            "sema_surumu": 1,
        }
        hedef = self.klasor / "bozuk_kanal.json"
        hedef.write_text(json.dumps(gecersiz_veri), encoding="utf-8")

        with self.assertRaises(ValueError):
            ders_json.oku(hedef)

    # 17. ders.json Şema Hatası (Eksik Parça Alanı)
    def test_17_ders_json_sema_hatasi_eksik_parca_alani(self):
        """Parçada 'sanal_bas' eksikse ValueError fırlatılır."""
        gecersiz_veri = {
            "ad": "ders1",
            "referans_kanal": "cam1",
            "kanallar": [
                {
                    "ad": "cam1",
                    "parcalar": [{"dosya": "01.mp4", "tur": "kayit"}],  # sanal_bas vs eksik
                }
            ],
            "senkron": {
                "yontem": "gcc_phat",
                "drift_ppm": 0.0,
                "guven": 1.0,
                "guvenilir": True,
                "neden": "ok",
            },
            "elle_isaretler": [],
            "sema_surumu": 1,
        }
        hedef = self.klasor / "bozuk_parca.json"
        hedef.write_text(json.dumps(gecersiz_veri), encoding="utf-8")

        with self.assertRaises(ValueError):
            ders_json.oku(hedef)

    # 18. ders.json Atomik Yazma
    def test_18_ders_json_atomik_yazma(self):
        """ders_json.yaz atomik tmp dosyası üzerinden başarıyla yazar ve tmp kalmaz."""
        ders = ders_json.DersTanimi(
            ad="atomik_test",
            kanallar=[],
            referans_kanal="yok",
            senkron=ders_json.SenkronBilgisi(
                yontem="tek_kanal",
                drift_ppm=0.0,
                guven=1.0,
                guvenilir=True,
                neden="ok",
            ),
        )
        hedef = self.klasor / "atomik.json"
        ders_json.yaz(hedef, ders)

        self.assertTrue(hedef.exists())
        # Temp dosyası kalmamış olmalı
        tmp_dosyalari = list(self.klasor.glob("*.tmp"))
        self.assertEqual(len(tmp_dosyalari), 0)

    # 19. Kanal Zamanı / Tersi Tutarlılığı (Drift = 0)
    def test_19_kanal_zamani_tersi_sifir_drift(self):
        """Drift olmadan kanal_zamani ve sanal_zaman karşılıklı tam tutarlıdır."""
        ders = ders_json.DersTanimi(
            ad="zaman_test",
            kanallar=[
                ders_json.Kanal(
                    ad="cam1",
                    parcalar=[],
                    ofset_sn=2.5,
                    drift_ppm=0.0,
                )
            ],
            referans_kanal="cam1",
            senkron=ders_json.SenkronBilgisi(),
        )

        sanal_t = 100.0
        yerel_t = ders_json.kanal_zamani(ders, "cam1", sanal_t)
        self.assertAlmostEqual(yerel_t, 102.5)

        geri_sanal = ders_json.sanal_zaman(ders, "cam1", yerel_t)
        self.assertAlmostEqual(geri_sanal, sanal_t)

        geri_sanal_alias = ders_json.kanal_zamani_tersi(ders, "cam1", yerel_t)
        self.assertAlmostEqual(geri_sanal_alias, sanal_t)

    # 20. Kanal Zamanı / Tersi Tutarlılığı (Drift Dahil)
    def test_20_kanal_zamani_tersi_drift_dahil(self):
        """Drift değeri varken kanal_zamani ve sanal_zaman karşılıklı tam tutarlıdır."""
        ders = ders_json.DersTanimi(
            ad="drift_test",
            kanallar=[
                ders_json.Kanal(
                    ad="cam2",
                    parcalar=[],
                    ofset_sn=-1.5,
                    drift_ppm=25.0,
                )
            ],
            referans_kanal="cam2",
            senkron=ders_json.SenkronBilgisi(),
        )

        for sanal_t in [0.0, 50.0, 120.35, 3600.0]:
            yerel_t = ders_json.kanal_zamani(ders, "cam2", sanal_t)
            geri_sanal = ders_json.sanal_zaman(ders, "cam2", yerel_t)
            self.assertAlmostEqual(geri_sanal, sanal_t, places=6)

        # C# portal eşleme formülüyle sabit değer doğrulaması (250 ppm drift, -2.0 sn ofset)
        ders_csharp = ders_json.DersTanimi(
            ad="csharp_eslesme",
            kanallar=[
                ders_json.Kanal(
                    ad="k2",
                    parcalar=[],
                    ofset_sn=-2.0,
                    drift_ppm=250.0,
                )
            ],
            referans_kanal="k2",
            senkron=ders_json.SenkronBilgisi(),
        )
        self.assertAlmostEqual(ders_json.kanal_zamani(ders_csharp, "k2", 1000.0), 998.25, places=5)
        self.assertAlmostEqual(ders_json.sanal_zaman(ders_csharp, "k2", 998.25), 1000.0, places=5)

    # 21. Kanal Zamanı Geçersiz Kanal Hatası
    def test_21_kanal_zamani_gecersiz_kanal(self):
        """Olmayan bir kanal sorgulandığında ValueError fırlatılır."""
        ders = ders_json.DersTanimi(
            ad="bos",
            kanallar=[],
            referans_kanal="",
            senkron=ders_json.SenkronBilgisi(),
        )
        with self.assertRaises(ValueError):
            ders_json.kanal_zamani(ders, "olmayan_kanal", 10.0)

    # 22. Boş Klasör Uyarı Listesi ve Çökme Yok
    def test_22_isle_grup_bos_klasor(self):
        """Boş klasör verildiğinde çökmez, uyarilar listesiyle basarisiz döner."""
        bos_dir = self.klasor / "bos_ders"
        bos_dir.mkdir()

        sonuc = grup.isle_grup(bos_dir)
        self.assertFalse(sonuc.basarili)
        self.assertTrue(len(sonuc.uyarilar) > 0)

    # 23. Eksik / Boş Kanal Uyarısı
    def test_23_isle_grup_eksik_bos_kanal_uyarisi(self):
        """Bir kanalı boş olan derste boş kanal için uyarı verilir, işlem çökmez."""
        (self.klasor / "cam1").mkdir()
        (self.klasor / "cam2").mkdir()  # boş kanal
        (self.klasor / "cam1" / "01.mp4").touch()

        def sahte_prob(d):
            return 0.0, 10.0

        def sahte_ses(d, sr=8000):
            return np.ones(10 * sr, dtype=np.float32)

        def sahte_transkript(ses):
            return [Segment(0.0, 5.0, "Test")]

        sonuc = grup.isle_grup(
            self.klasor,
            prob_fn=sahte_prob,
            ses_yukle_fn=sahte_ses,
            transkript_fn=sahte_transkript,
        )

        self.assertTrue(sonuc.basarili)
        uyari_metinleri = " ".join(sonuc.uyarilar)
        self.assertIn("cam2", uyari_metinleri)

    # 24. Grup Tam Akış ve Çıktı Dosyaları
    def test_24_isle_grup_tam_akis_ve_ciktilar(self):
        """isle_grup srt, vtt, txt, json ve ders.json dosyalarını eksiksiz üretir."""
        (self.klasor / "cam1").mkdir()
        (self.klasor / "cam1" / "01.mp4").touch()

        def sahte_prob(d):
            return 0.0, 15.0

        def sahte_ses(d, sr=8000):
            return np.ones(15 * sr, dtype=np.float32)

        def sahte_transkript(ses):
            return [
                Segment(0.0, 5.0, "Birinci segment"),
                Segment(5.0, 10.0, "İkinci segment"),
            ]

        sonuc = grup.isle_grup(
            self.klasor,
            prob_fn=sahte_prob,
            ses_yukle_fn=sahte_ses,
            transkript_fn=sahte_transkript,
        )

        self.assertTrue(sonuc.basarili)
        cikis = sonuc.cikis_klasoru
        self.assertEqual(cikis.name, f"{self.klasor.name}_islenmis")
        self.assertTrue((cikis / f"{self.klasor.name}.srt").exists())
        self.assertTrue((cikis / f"{self.klasor.name}.vtt").exists())
        self.assertTrue((cikis / f"{self.klasor.name}.txt").exists())
        self.assertTrue((cikis / f"{self.klasor.name}.json").exists())
        self.assertTrue((cikis / "ders.json").exists())

        # json v2 kontrolü
        meta = json.loads((cikis / f"{self.klasor.name}.json").read_text(encoding="utf-8"))
        self.assertEqual(meta["sema_surumu"], 2)
        self.assertEqual(meta["segment_sayisi"], 2)

    # 25. Çok Kanallı ve İlişkisiz Kanal ders.json'a Güvenilmez İşlenir
    def test_25_iliskisiz_kanal_ders_jsonda_guvenilmez(self):
        """İlişkisiz kanalın senkronu ders.json'da guvenilir=False olarak kaydedilir."""
        (self.klasor / "cam_ref").mkdir()
        (self.klasor / "cam_gurultu").mkdir()
        (self.klasor / "cam_ref" / "01.mp4").touch()
        (self.klasor / "cam_gurultu" / "01.mp4").touch()

        sr = 8000
        ref_dalga = _sentetik_konusma(12.0, sr=sr, tohum=42)
        rng = np.random.default_rng(123)
        gurultu_dalga = rng.standard_normal(len(ref_dalga)).astype(np.float32) * 0.05

        def sahte_prob(d):
            return 0.0, 12.0

        def sahte_ses(d, sample_rate=8000):
            if "cam_ref" in str(d):
                return ref_dalga
            return gurultu_dalga

        def sahte_transkript(ses):
            return [Segment(0.0, 5.0, "Konuşma")]

        sonuc = grup.isle_grup(
            self.klasor,
            referans_kanal_adi="cam_ref",
            prob_fn=sahte_prob,
            ses_yukle_fn=sahte_ses,
            transkript_fn=sahte_transkript,
        )

        self.assertTrue(sonuc.basarili)
        ders = ders_json.oku(sonuc.cikis_klasoru / "ders.json")
        gurultu_kanal = next((k for k in ders.kanallar if k.ad == "cam_gurultu"), None)
        self.assertIsNotNone(gurultu_kanal)
        self.assertFalse(gurultu_kanal.guvenilir)

    # 26. Aynı Olayı Eşleme (Pozitif Gecikmede Yerel Zaman)
    def test_26_ayni_olay_esleme_pozitif_gecikme(self):
        """0.1 sn geciken ses için sanal 0.0 anındaki olay yerel 0.1 sn'de eşlenir."""
        ders = ders_json.DersTanimi(
            ad="olay_esleme_test",
            kanallar=[
                ders_json.Kanal(
                    ad="cam_gecikmeli",
                    parcalar=[],
                    ofset_sn=0.1,
                    drift_ppm=0.0,
                )
            ],
            referans_kanal="cam_ref",
            senkron=ders_json.SenkronBilgisi(),
        )

        # 0.1 sn geciken kanalda sanal 0 anındaki olay yerel 0.1 sn'dedir
        yerel_olay = ders_json.kanal_zamani(ders, "cam_gecikmeli", 0.0)
        self.assertAlmostEqual(yerel_olay, 0.1, places=5)

        # Yerel 0.1 sn'deki olay sanal 0.0 anına eşlenmelidir
        sanal_olay = ders_json.sanal_zaman(ders, "cam_gecikmeli", 0.1)
        self.assertAlmostEqual(sanal_olay, 0.0, places=5)

        # Farklı zaman noktalarında aynı olayın eşlendiği doğrulanır
        for t in [1.0, 5.5, 60.0]:
            y = ders_json.kanal_zamani(ders, "cam_gecikmeli", t)
            self.assertAlmostEqual(y, t + 0.1, places=5)
            self.assertAlmostEqual(ders_json.sanal_zaman(ders, "cam_gecikmeli", y), t, places=5)

    # 27. Varsayılan Transkript Geçici Klasör ve Temizlik
    def test_27_varsayilan_transkript_gecici_klasor_temizligi(self):
        """_varsayilan_transkript ara çıktıları geçici klasörde üretip temizler, cikis_dir'i kirletmez."""
        from unittest.mock import patch
        from videohat.hat import Sonuc

        ref_ses = np.zeros(8000, dtype=np.float32)
        cikis_dir = self.klasor / "temiz_cikis"
        cikis_dir.mkdir()

        gecici_yollar_yakalandi = []

        def sahte_isle_tek(video, hedef_cikis, ayar, bayraklar):
            hedef_path = Path(hedef_cikis)
            gecici_yollar_yakalandi.append(hedef_path)
            # hat.isle_tek gibi sahte çıktılar oluştur
            (hedef_path / f"{video.stem}.srt").write_text("1\n00:00:00,000 --> 00:00:01,000\nTest", encoding="utf-8")
            (hedef_path / f"{video.stem}.txt").write_text("Test", encoding="utf-8")
            (hedef_path / f"{video.stem}.vtt").write_text("WEBVTT\n", encoding="utf-8")
            meta = {
                "sema_surumu": 2,
                "segmentler": [{"bas": 0.0, "son": 1.0, "metin": "Test"}],
            }
            (hedef_path / f"{video.stem}.json").write_text(json.dumps(meta), encoding="utf-8")
            return Sonuc(ad=video.stem, basarili=True, hata=None)

        with patch("videohat.hat.isle_tek", side_effect=sahte_isle_tek):
            segmentler = grup._varsayilan_transkript(ref_ses, cikis_dir, None, None)

        self.assertEqual(len(segmentler), 1)
        self.assertEqual(segmentler[0].metin, "Test")

        # hat.isle_tek cikis_dir'e değil, geçici bir dizine yönlendirilmiş olmalı
        self.assertEqual(len(gecici_yollar_yakalandi), 1)
        gecici_dir = gecici_yollar_yakalandi[0]
        self.assertNotEqual(gecici_dir, cikis_dir)
        # Geçici dizin işlem bitince temizlenmiş (silinmiş) olmalı
        self.assertFalse(gecici_dir.exists())

        # cikis_dir içinde hiçbir ara dosya (wav, srt, vtt, txt, json) kalmamış olmalı
        kalan_dosyalar = list(cikis_dir.iterdir())
        self.assertEqual(len(kalan_dosyalar), 0)

    # 28. isle_grup Varsayılan Transkript Akışı ve Temiz Çıktı Klasörü
    def test_28_isle_grup_gercek_transkript_yolu_cikis_temiz(self):
        """transkript_fn enjekte edilmediğinde isle_grup ara çıktılardan arındırılmış temiz klasör üretir."""
        from unittest.mock import patch
        from videohat.hat import Sonuc

        (self.klasor / "tek_cam").mkdir()
        (self.klasor / "tek_cam" / "01.mp4").touch()

        def sahte_prob(d):
            return 0.0, 5.0

        def sahte_ses(d, sr=8000):
            return np.zeros(5 * sr, dtype=np.float32)

        def sahte_isle_tek(video, hedef_cikis, ayar, bayraklar):
            hedef_path = Path(hedef_cikis)
            (hedef_path / f"{video.stem}.srt").write_text("1\n00:00:00,000 --> 00:00:01,000\nMerhaba", encoding="utf-8")
            (hedef_path / f"{video.stem}.txt").write_text("Merhaba", encoding="utf-8")
            (hedef_path / f"{video.stem}.vtt").write_text("WEBVTT\n", encoding="utf-8")
            meta = {
                "sema_surumu": 2,
                "segmentler": [{"bas": 0.0, "son": 1.0, "metin": "Merhaba"}],
            }
            (hedef_path / f"{video.stem}.json").write_text(json.dumps(meta), encoding="utf-8")
            return Sonuc(ad=video.stem, basarili=True, hata=None)

        with patch("videohat.hat.isle_tek", side_effect=sahte_isle_tek):
            sonuc = grup.isle_grup(
                self.klasor,
                prob_fn=sahte_prob,
                ses_yukle_fn=sahte_ses,
                transkript_fn=None,  # Gerçek _varsayilan_transkript yolunu çalıştır
            )

        self.assertTrue(sonuc.basarili)
        cikis = sonuc.cikis_klasoru
        beklenen_dosyalar = {
            f"{self.klasor.name}.srt",
            f"{self.klasor.name}.vtt",
            f"{self.klasor.name}.txt",
            f"{self.klasor.name}.json",
            "ders.json",
        }
        mevcut_dosyalar = {f.name for f in cikis.iterdir()}
        # Çıktı klasöründe yalnız beklenen nihai dosyalar bulunmalı, hiçbir ara tmp/wav/json kalmamalı
        self.assertEqual(mevcut_dosyalar, beklenen_dosyalar)

    # 29. Varsayılan Transkript isle_tek Hatasını Yukarı Taşır
    def test_29_varsayilan_transkript_isle_tek_hatasi_yukari_tasinir(self):
        """hat.isle_tek başarısız olduğunda _varsayilan_transkript hatayı yutmaz, RuntimeError fırlatır."""
        from unittest.mock import patch
        from videohat.hat import Sonuc

        ref_ses = np.zeros(8000, dtype=np.float32)
        cikis_dir = self.klasor / "hata_cikis"
        cikis_dir.mkdir()

        def sahte_isle_tek_basarisiz(video, hedef_cikis, ayar, bayraklar):
            return Sonuc(ad=video.stem, basarili=False, hata="STT modeli CUDA OOM")

        with patch("videohat.hat.isle_tek", side_effect=sahte_isle_tek_basarisiz):
            with self.assertRaises(RuntimeError) as ctx:
                grup._varsayilan_transkript(ref_ses, cikis_dir, None, None)
            self.assertIn("STT modeli CUDA OOM", str(ctx.exception))

        # cikis_dir kirletilmemiş olmalı
        self.assertEqual(list(cikis_dir.iterdir()), [])

    # 30. isle_grup isle_tek Başarısız Olunca Başarısız Sonuç Döndürür
    def test_30_isle_grup_isle_tek_basarisiz_olunca_hata_dondurur(self):
        """isle_tek başarısız olduğunda isle_grup basarili=False ve hata mesajı ile döner."""
        from unittest.mock import patch
        from videohat.hat import Sonuc

        (self.klasor / "tek_cam").mkdir()
        (self.klasor / "tek_cam" / "01.mp4").touch()

        def sahte_prob(d):
            return 0.0, 5.0

        def sahte_ses(d, sr=8000):
            return np.zeros(5 * sr, dtype=np.float32)

        def sahte_isle_tek_basarisiz(video, hedef_cikis, ayar, bayraklar):
            return Sonuc(ad=video.stem, basarili=False, hata="Whisper bellek yetersiz")

        with patch("videohat.hat.isle_tek", side_effect=sahte_isle_tek_basarisiz):
            sonuc = grup.isle_grup(
                self.klasor,
                prob_fn=sahte_prob,
                ses_yukle_fn=sahte_ses,
                transkript_fn=None,  # Gerçek _varsayilan_transkript yolunu çalıştır
            )

        self.assertFalse(sonuc.basarili)
        self.assertIn("Whisper bellek yetersiz", str(sonuc.hata))
        uyari_metin = " ".join(sonuc.uyarilar)
        self.assertIn("Whisper bellek yetersiz", uyari_metin)

        # Başarısızlıkta boş srt/txt/vtt çıktıları oluşturulmamalı
        cikis = sonuc.cikis_klasoru
        if cikis.exists():
            srt_dosyasi = cikis / f"{self.klasor.name}.srt"
            self.assertFalse(srt_dosyasi.exists())

    # 31. isle_grup Enjekte Transkript Fonksiyonu Hatası
    def test_31_isle_grup_enjekte_transkript_hatasi(self):
        """Enjekte edilen transkript_fn istisna fırlattığında isle_grup çökmeyip basarili=False döner."""
        (self.klasor / "cam1").mkdir()
        (self.klasor / "cam1" / "01.mp4").touch()

        def sahte_prob(d):
            return 0.0, 5.0

        def sahte_ses(d, sr=8000):
            return np.zeros(5 * sr, dtype=np.float32)

        def hatali_transkript(ses):
            raise ValueError("Harici STT servisi ulaşılamaz")

        sonuc = grup.isle_grup(
            self.klasor,
            prob_fn=sahte_prob,
            ses_yukle_fn=sahte_ses,
            transkript_fn=hatali_transkript,
        )

        self.assertFalse(sonuc.basarili)
        self.assertIn("Harici STT servisi ulaşılamaz", str(sonuc.hata))
        uyari_metin = " ".join(sonuc.uyarilar)
        self.assertIn("Harici STT servisi ulaşılamaz", uyari_metin)

    # 32. Sayısız Dosyalar ve Ses Uzantılarıyla Kanal Bulma (Kusur 1)
    def test_32_kanallari_bul_sayisiz_ve_ses_uzantilari(self):
        """Düz klasörde ders.mp4 tek kanal olur; cam1.mp3 ve cam2.m4a ses uzantılarıyla bulunur."""
        (self.klasor / "ders.mp4").touch()
        kanallar = grup.kanallari_bul(self.klasor)
        self.assertIn("ders", kanallar)
        self.assertEqual(len(kanallar["ders"]), 1)

        klasor2 = Path(tempfile.mkdtemp())
        try:
            (klasor2 / "cam1.mp3").touch()
            (klasor2 / "cam2.m4a").touch()
            kanallar2 = grup.kanallari_bul(klasor2)
            self.assertIn("cam", kanallar2)
            self.assertEqual(len(kanallar2["cam"]), 2)
        finally:
            shutil.rmtree(klasor2, ignore_errors=True)

    # 33. Referans Kanal Seçiminde RMS ile Sessiz Mikrofonun Elenmesi (Kusur 2)
    def test_33_referans_sec_rms_sessiz_kanal_elenir(self):
        """Alfabetik sırada önce gelse bile sessiz kanal yerine enerjili kanal seçilir."""
        sessiz = np.zeros(8000, dtype=np.float32)
        sesli = _sentetik_konusma(1.0, sr=8000, tohum=42)

        # a_sessiz alfabetik olarak b_sesli'den önde
        kanallar = {"a_sessiz": sessiz, "b_sesli": sesli}
        secilen = grup.referans_sec(kanallar)
        self.assertEqual(secilen, "b_sesli")

    # 34. hat.isle_tek Üstveri Alanlarının Grup JSON Çıktısına Aktarımı (Kusur 6)
    def test_34_isle_grup_meta_alanlari_aktarimi(self):
        """hat.isle_tek'in ürettiği cihaz, model, compute_type ve süreler alanları son JSON'a aktarılır."""
        from unittest.mock import patch
        from videohat.hat import Bayraklar, Sonuc

        (self.klasor / "tek_cam").mkdir()
        (self.klasor / "tek_cam" / "01.mp4").touch()

        def sahte_prob(d):
            return 0.0, 5.0

        def sahte_ses(d, sr=8000):
            return np.zeros(5 * sr, dtype=np.float32)

        def sahte_isle_tek(video, hedef_cikis, ayar, bayraklar):
            hedef_path = Path(hedef_cikis)
            meta = {
                "sema_surumu": 2,
                "cihaz": "cuda",
                "compute_type": "float16",
                "model": "large-v3",
                "neden": "gpu_bellek_yeterli",
                "sureler": {"stt": 4.2, "ses_cikar": 0.8},
                "segmentler": [{"bas": 0.0, "son": 2.0, "metin": "Test"}],
            }
            (hedef_path / f"{video.stem}.json").write_text(json.dumps(meta), encoding="utf-8")
            return Sonuc(ad=video.stem, basarili=True, hata=None)

        bay = Bayraklar(model="large-v3", cihaz="cuda")
        with patch("videohat.hat.isle_tek", side_effect=sahte_isle_tek):
            sonuc = grup.isle_grup(
                self.klasor,
                bayraklar=bay,
                prob_fn=sahte_prob,
                ses_yukle_fn=sahte_ses,
            )

        self.assertTrue(sonuc.basarili)
        json_icerik = json.loads((sonuc.cikis_klasoru / f"{self.klasor.name}.json").read_text(encoding="utf-8"))
        self.assertEqual(json_icerik["cihaz"], "cuda")
        self.assertEqual(json_icerik["compute_type"], "float16")
        self.assertEqual(json_icerik["model"], "large-v3")
        self.assertEqual(json_icerik["sureler"]["stt"], 4.2)


if __name__ == "__main__":
    unittest.main()
