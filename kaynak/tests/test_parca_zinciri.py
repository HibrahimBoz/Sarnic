"""
Parça zinciri birim testleri.
Sanal zaman çizelgesi, sıralama, boşluk, çakışma ve iki yönlü eşleme testleri.
"""

import math
import sys
import time
import unittest
from pathlib import Path

# Modül arama yolunu ayarla
kok_dizin = Path(__file__).resolve().parent.parent
app_dizin = kok_dizin / "app"

for p in (str(kok_dizin), str(app_dizin)):
    if p not in sys.path:
        sys.path.insert(0, p)

try:
    from app.sarnicatolye.parca_zinciri import (
        Dilim,
        Parca,
        SanalZaman,
        SanalZamanCizelgesi,
        cizelge_olustur,
        parca_zinciri_olustur,
        zincir_olustur,
        dogal_siralama_anahtari,
    )
except ImportError:
    from sarnicatolye.parca_zinciri import (
        Dilim,
        Parca,
        SanalZaman,
        SanalZamanCizelgesi,
        cizelge_olustur,
        parca_zinciri_olustur,
        zincir_olustur,
        dogal_siralama_anahtari,
    )


class TestParcaZinciri(unittest.TestCase):
    """Parça zinciri için unittest testleri (≥ 20 test)."""

    def test_01_tek_parca(self):
        """Tek bir parça ile çizelge oluşturma ve alanların doğruluğu."""
        parcalar = [Parca("kamera1_001.mp4", 1000.0, 15.0)]
        cizelge = SanalZamanCizelgesi(parcalar)

        self.assertEqual(len(cizelge), 1)
        self.assertAlmostEqual(cizelge.toplam_sure, 15.0)

        dilim = cizelge[0]
        self.assertEqual(dilim.dosya, "kamera1_001.mp4")
        self.assertAlmostEqual(dilim.sanal_bas, 0.0)
        self.assertAlmostEqual(dilim.sanal_son, 15.0)
        self.assertAlmostEqual(dilim.yerel_bas, 0.0)
        self.assertEqual(dilim.tur, "kayit")

    def test_02_sirali_bitisik_zamansiz(self):
        """Zaman bilgisi olmayan bitişik parçaların sıralanması."""
        parcalar = [
            Parca("ders_1.mp4", None, 10.0),
            Parca("ders_2.mp4", None, 20.0),
            Parca("ders_3.mp4", None, 15.0),
        ]
        cizelge = SanalZamanCizelgesi(parcalar)

        self.assertEqual(len(cizelge), 3)
        self.assertAlmostEqual(cizelge.toplam_sure, 45.0)

        self.assertEqual(cizelge[0].dosya, "ders_1.mp4")
        self.assertAlmostEqual(cizelge[0].sanal_bas, 0.0)
        self.assertAlmostEqual(cizelge[0].sanal_son, 10.0)

        self.assertEqual(cizelge[1].dosya, "ders_2.mp4")
        self.assertAlmostEqual(cizelge[1].sanal_bas, 10.0)
        self.assertAlmostEqual(cizelge[1].sanal_son, 30.0)

        self.assertEqual(cizelge[2].dosya, "ders_3.mp4")
        self.assertAlmostEqual(cizelge[2].sanal_bas, 30.0)
        self.assertAlmostEqual(cizelge[2].sanal_son, 45.0)

    def test_03_sirali_bitisik_zamanli(self):
        """Zaman damgalı tam bitişik parçaların (fark = 0) çizelgesi."""
        parcalar = [
            Parca("part1.mp4", 100.0, 10.0),
            Parca("part2.mp4", 110.0, 20.0),
            Parca("part3.mp4", 130.0, 30.0),
        ]
        cizelge = SanalZamanCizelgesi(parcalar)

        self.assertEqual(len(cizelge), 3)
        self.assertAlmostEqual(cizelge.toplam_sure, 60.0)
        for d in cizelge:
            self.assertEqual(d.tur, "kayit")

    def test_04_kucuk_bosluk_esigi_altinda(self):
        """Boşluk <= 0.5 s olduğunda boşluk dilimi açılmamalıdır (bitişik kabul edilir)."""
        parcalar = [
            Parca("part1.mp4", 100.0, 10.0),      # biter: 110.0
            Parca("part2.mp4", 110.3, 10.0),      # fark = 0.3 s <= 0.5 s
            Parca("part3.mp4", 120.8, 10.0),      # fark = 0.5 s <= 0.5 s
        ]
        cizelge = SanalZamanCizelgesi(parcalar)

        # Hiçbiri boşluk dilimi üretmemeli
        self.assertEqual(len(cizelge), 3)
        self.assertTrue(all(d.tur == "kayit" for d in cizelge))
        self.assertAlmostEqual(cizelge[0].sanal_son, cizelge[1].sanal_bas)
        self.assertAlmostEqual(cizelge[1].sanal_son, cizelge[2].sanal_bas)
        self.assertAlmostEqual(cizelge.toplam_sure, 30.0)

    def test_05_bosluklu_parcalar(self):
        """Boşluk > 0.5 s olduğunda dosyasız 'bosluk' dilimi oluşturulmalıdır."""
        parcalar = [
            Parca("part1.mp4", 100.0, 10.0),      # biter: 110.0
            Parca("part2.mp4", 112.5, 10.0),      # fark = 2.5 s > 0.5 s
        ]
        cizelge = SanalZamanCizelgesi(parcalar)

        self.assertEqual(len(cizelge), 3)
        # 1. Kayıt
        self.assertEqual(cizelge[0].tur, "kayit")
        self.assertEqual(cizelge[0].dosya, "part1.mp4")
        self.assertAlmostEqual(cizelge[0].sanal_bas, 0.0)
        self.assertAlmostEqual(cizelge[0].sanal_son, 10.0)

        # 2. Boşluk (dosyasız)
        self.assertEqual(cizelge[1].tur, "bosluk")
        self.assertIsNone(cizelge[1].dosya)
        self.assertAlmostEqual(cizelge[1].sanal_bas, 10.0)
        self.assertAlmostEqual(cizelge[1].sanal_son, 12.5)
        self.assertAlmostEqual(cizelge[1].sure, 2.5)

        # 3. Kayıt
        self.assertEqual(cizelge[2].tur, "kayit")
        self.assertEqual(cizelge[2].dosya, "part2.mp4")
        self.assertAlmostEqual(cizelge[2].sanal_bas, 12.5)
        self.assertAlmostEqual(cizelge[2].sanal_son, 22.5)

        self.assertAlmostEqual(cizelge.toplam_sure, 22.5)

    def test_06_birden_fazla_bosluk(self):
        """Birden çok boşluk içeren parçaların sıralı çizelgesi."""
        parcalar = [
            Parca("a.mp4", 100.0, 5.0),    # 100 - 105
            Parca("b.mp4", 106.0, 5.0),    # fark = 1.0 > 0.5 -> 1 s boşluk
            Parca("c.mp4", 114.0, 5.0),    # 111 - 114 fark = 3.0 > 0.5 -> 3 s boşluk
        ]
        cizelge = SanalZamanCizelgesi(parcalar)

        self.assertEqual(len(cizelge), 5)
        turler = [d.tur for d in cizelge]
        self.assertEqual(turler, ["kayit", "bosluk", "kayit", "bosluk", "kayit"])
        self.assertAlmostEqual(cizelge.toplam_sure, 5.0 + 1.0 + 5.0 + 3.0 + 5.0)

    def test_07_cakismali_parcalar(self):
        """Kısmi çakışmada sonraki parça başı kırpılmalı ve tür 'cakisma_kirpildi' olmalıdır."""
        parcalar = [
            Parca("part1.mp4", 100.0, 10.0),    # biter: 110.0
            Parca("part2.mp4", 108.0, 6.0),     # çakışma = 2.0 s (< 6.0 s)
        ]
        cizelge = SanalZamanCizelgesi(parcalar)

        self.assertEqual(len(cizelge), 2)
        # İlk parça normal
        self.assertEqual(cizelge[0].tur, "kayit")
        self.assertEqual(cizelge[0].dosya, "part1.mp4")
        self.assertAlmostEqual(cizelge[0].sanal_bas, 0.0)
        self.assertAlmostEqual(cizelge[0].sanal_son, 10.0)
        self.assertAlmostEqual(cizelge[0].yerel_bas, 0.0)

        # İkinci parça kırpılmış
        d2 = cizelge[1]
        self.assertEqual(d2.tur, "cakisma_kirpildi")
        self.assertEqual(d2.dosya, "part2.mp4")
        self.assertAlmostEqual(d2.yerel_bas, 2.0)  # Baştan 2 sn kırpıldı
        self.assertAlmostEqual(d2.sanal_bas, 10.0)
        self.assertAlmostEqual(d2.sanal_son, 14.0) # Kalan 4 sn
        self.assertAlmostEqual(d2.sure, 4.0)

        self.assertAlmostEqual(cizelge.toplam_sure, 14.0)

    def test_08_tamamen_iceren_cakisma(self):
        """Çakışma parça süresinden büyükse parça atlanmalı ve uyarı yazılmalıdır."""
        parcalar = [
            Parca("part1.mp4", 100.0, 20.0),    # biter: 120.0
            Parca("part2.mp4", 105.0, 5.0),     # çakışma = 15.0 s >= 5.0 s (tamamen içeride)
            Parca("part3.mp4", 125.0, 10.0),    # boşluk = 5.0 s sonra normal
        ]
        cizelge = SanalZamanCizelgesi(parcalar)

        # part2 atlanmış olmalı
        dosyalar = [d.dosya for d in cizelge if d.dosya is not None]
        self.assertNotIn("part2.mp4", dosyalar)
        self.assertEqual(dosyalar, ["part1.mp4", "part3.mp4"])

        # Uyarı listesinde yer almalı
        self.assertTrue(any("part2.mp4" in u for u in cizelge.uyarilar))

    def test_09_cakisma_esit_sure(self):
        """Çakışma tam olarak parça süresine eşit olduğunda da parça atlanmalıdır."""
        parcalar = [
            Parca("part1.mp4", 100.0, 10.0),    # biter: 110.0
            Parca("part2.mp4", 106.0, 4.0),     # çakışma = 4.0 s == süre 4.0 s
        ]
        cizelge = SanalZamanCizelgesi(parcalar)

        self.assertEqual(len(cizelge), 1)
        self.assertEqual(cizelge[0].dosya, "part1.mp4")
        self.assertTrue(any("part2.mp4" in u for u in cizelge.uyarilar))

    def test_10_pes_pese_cakismalar(self):
        """Ardışık çakışma zinciri durumunda kırpılma ve zaman takibi."""
        parcalar = [
            Parca("a.mp4", 100.0, 10.0),   # 100 - 110 -> [0, 10]
            Parca("b.mp4", 107.0, 6.0),    # çakışma 3.0 s -> biter 113.0 -> [10, 13] yerel 3.0
            Parca("c.mp4", 111.0, 5.0),    # çakışma 2.0 s (113 - 111) -> biter 116.0 -> [13, 16] yerel 2.0
        ]
        cizelge = SanalZamanCizelgesi(parcalar)

        self.assertEqual(len(cizelge), 3)
        self.assertEqual(cizelge[0].tur, "kayit")
        self.assertEqual(cizelge[1].tur, "cakisma_kirpildi")
        self.assertEqual(cizelge[2].tur, "cakisma_kirpildi")

        self.assertAlmostEqual(cizelge[1].yerel_bas, 3.0)
        self.assertAlmostEqual(cizelge[2].yerel_bas, 2.0)
        self.assertAlmostEqual(cizelge.toplam_sure, 16.0)

    def test_11_zamani_olmayan_ad_sirasi(self):
        """Zamanı olmayan parçalarda doğal sıralama ('ders_2' < 'ders_10')."""
        parcalar = [
            Parca("ders_10.mp4", None, 5.0),
            Parca("ders_2.mp4", None, 5.0),
            Parca("ders_1.mp4", None, 5.0),
            Parca("ders_20.mp4", None, 5.0),
        ]
        cizelge = SanalZamanCizelgesi(parcalar)

        dosya_sirasi = [d.dosya for d in cizelge]
        self.assertEqual(
            dosya_sirasi,
            ["ders_1.mp4", "ders_2.mp4", "ders_10.mp4", "ders_20.mp4"]
        )

    def test_12_sure_sifir_atlanir(self):
        """Süresi 0 olan parçalar çizelgeye dahil edilmeyip atlanmalıdır."""
        parcalar = [
            Parca("gecerli.mp4", 100.0, 10.0),
            Parca("sifir.mp4", 115.0, 0.0),
        ]
        cizelge = SanalZamanCizelgesi(parcalar)

        self.assertEqual(len(cizelge), 1)
        self.assertEqual(cizelge[0].dosya, "gecerli.mp4")
        self.assertTrue(any("sifir.mp4" in u for u in cizelge.uyarilar))

    def test_13_sure_negatif_atlanir(self):
        """Süresi negatif olan parçalar atlanmalıdır."""
        parcalar = [
            Parca("negatif.mp4", 100.0, -5.0),
            Parca("gecerli.mp4", 105.0, 8.0),
        ]
        cizelge = SanalZamanCizelgesi(parcalar)

        self.assertEqual(len(cizelge), 1)
        self.assertEqual(cizelge[0].dosya, "gecerli.mp4")
        self.assertTrue(any("negatif.mp4" in u for u in cizelge.uyarilar))

    def test_14_karisik_durumlar(self):
        """Sıfır süre, boşluk, çakışma ve tam örtüşme içeren karmaşık senaryo."""
        parcalar = [
            Parca("gecersiz.mp4", 50.0, 0.0),
            Parca("p1.mp4", 100.0, 10.0),         # 100 - 110 [0, 10]
            Parca("p2_icinde.mp4", 102.0, 3.0),   # çakışma 8 s >= 3 s -> atla
            Parca("p3_kirp.mp4", 108.0, 6.0),     # çakışma 2 s < 6 s -> yerel_bas 2.0, [10, 14]
            Parca("p4_bosluk.mp4", 117.0, 5.0),   # son 114.0, fark 3.0 > 0.5 -> boşluk 3 s, [14, 17] boşluk, [17, 22] p4
        ]
        cizelge = SanalZamanCizelgesi(parcalar)

        self.assertAlmostEqual(cizelge.toplam_sure, 22.0)
        self.assertEqual(len(cizelge), 4)  # p1, p3, bosluk, p4
        self.assertEqual(cizelge[0].dosya, "p1.mp4")
        self.assertEqual(cizelge[1].dosya, "p3_kirp.mp4")
        self.assertIsNone(cizelge[2].dosya)
        self.assertEqual(cizelge[3].dosya, "p4_bosluk.mp4")

    def test_15_bos_liste(self):
        """Boş parça listesi verildiğinde boş çizelge ve 0 süre dönmeli."""
        cizelge = SanalZamanCizelgesi([])
        self.assertEqual(len(cizelge), 0)
        self.assertAlmostEqual(cizelge.toplam_sure, 0.0)

    def test_16_esleme_sanal_to_yerel_normal(self):
        """Normal kayıt dilimi içinde sanal zamandan yerel zamana eşleme."""
        parcalar = [
            Parca("a.mp4", 100.0, 10.0),
            Parca("b.mp4", 110.0, 15.0),
        ]
        cizelge = SanalZamanCizelgesi(parcalar)

        dosya, yerel = cizelge.sanal_to_yerel(4.5)
        self.assertEqual(dosya, "a.mp4")
        self.assertAlmostEqual(yerel, 4.5)

        dosya, yerel = cizelge.sanal_to_yerel(16.0)
        self.assertEqual(dosya, "b.mp4")
        self.assertAlmostEqual(yerel, 6.0)

    def test_17_esleme_sanal_to_yerel_bosluk(self):
        """Boşluk dilimi içinde eşleme dosya=None ve ofset dönmelidir."""
        parcalar = [
            Parca("a.mp4", 100.0, 10.0),
            Parca("b.mp4", 115.0, 10.0),  # 5 s boşluk [10.0, 15.0]
        ]
        cizelge = SanalZamanCizelgesi(parcalar)

        dosya, yerel = cizelge.sanal_to_yerel(12.5)
        self.assertIsNone(dosya)
        self.assertAlmostEqual(yerel, 2.5)

    def test_18_esleme_sanal_to_yerel_kirpilmis(self):
        """Çakışma nedeniyle kırpılan parçada yerel_bas ofsetinin eklenmesi."""
        parcalar = [
            Parca("a.mp4", 100.0, 10.0),
            Parca("b.mp4", 107.0, 10.0),  # çakışma 3.0 s, yerel_bas 3.0, sanal [10.0, 17.0]
        ]
        cizelge = SanalZamanCizelgesi(parcalar)

        dosya, yerel = cizelge.sanal_to_yerel(12.0)
        self.assertEqual(dosya, "b.mp4")
        # sanal 12.0 -> dilim başlangıcından 2.0 s sonra -> yerel_bas (3.0) + 2.0 = 5.0 s
        self.assertAlmostEqual(yerel, 5.0)

    def test_19_esleme_sinir_noktalari_ileri_parca(self):
        """Dilim sınırlarında tam kesişim anında ileri parçanın seçilmesi kuralı."""
        parcalar = [
            Parca("a.mp4", 100.0, 10.0),   # sanal [0.0, 10.0]
            Parca("b.mp4", 110.0, 10.0),   # sanal [10.0, 20.0]
            Parca("c.mp4", 120.0, 10.0),   # sanal [20.0, 30.0]
        ]
        cizelge = SanalZamanCizelgesi(parcalar)

        # 0.0 noktası -> ilk dilim
        dosya, yerel = cizelge.sanal_to_yerel(0.0)
        self.assertEqual(dosya, "a.mp4")
        self.assertAlmostEqual(yerel, 0.0)

        # 10.0 tam sınır noktası -> ileri parça (b.mp4) seçilmeli
        dosya, yerel = cizelge.sanal_to_yerel(10.0)
        self.assertEqual(dosya, "b.mp4")
        self.assertAlmostEqual(yerel, 0.0)

        # 20.0 tam sınır noktası -> ileri parça (c.mp4) seçilmeli
        dosya, yerel = cizelge.sanal_to_yerel(20.0)
        self.assertEqual(dosya, "c.mp4")
        self.assertAlmostEqual(yerel, 0.0)

        # 30.0 toplam süre (en son nokta) -> son dilimin sonu
        dosya, yerel = cizelge.sanal_to_yerel(30.0)
        self.assertEqual(dosya, "c.mp4")
        self.assertAlmostEqual(yerel, 10.0)

    def test_20_esleme_gidis_donus(self):
        """Kayıt dilimleri için sanal->yerel ve yerel->sanal gidiş-dönüş tutarlılığı."""
        parcalar = [
            Parca("cam_a.mp4", 100.0, 12.0),
            Parca("cam_b.mp4", 115.0, 8.0),   # 3 s boşluk
            Parca("cam_c.mp4", 121.0, 10.0),  # 2 s çakışma kırpıldı
        ]
        cizelge = SanalZamanCizelgesi(parcalar)

        ornek_noktalar = [1.5, 6.0, 11.9, 16.0, 21.0, 24.5]
        for sn in ornek_noktalar:
            dosya, yerel = cizelge.sanal_to_yerel(sn)
            if dosya is not None:
                geri_sanal = cizelge.yerel_to_sanal(dosya, yerel)
                self.assertAlmostEqual(sn, geri_sanal, places=5)

    def test_21_esleme_sinir_disi_hatalar(self):
        """Geçersiz sanal zaman veya yerel zaman girildiğinde ValueError fırlatılması."""
        parcalar = [Parca("video.mp4", 100.0, 10.0)]
        cizelge = SanalZamanCizelgesi(parcalar)

        with self.assertRaises(ValueError):
            cizelge.sanal_to_yerel(-1.0)

        with self.assertRaises(ValueError):
            cizelge.sanal_to_yerel(11.0)

        with self.assertRaises(ValueError):
            cizelge.yerel_to_sanal("video.mp4", -0.5)

        with self.assertRaises(ValueError):
            cizelge.yerel_to_sanal("video.mp4", 10.5)

        with self.assertRaises(ValueError):
            cizelge.yerel_to_sanal("olmayan_dosya.mp4", 2.0)

    def test_22_performans_3_kanal_40_parca(self):
        """3 kanal × 40 parça çizelge oluşturma ve binlerce arama < 1 saniye sürmelidir."""
        baslama = time.perf_counter()

        kanallar = ["on_kamera", "yan_kamera", "yazi_tahtasi"]
        cizelgeler = []

        for kanal in kanallar:
            parcalar = []
            zaman = 1000.0
            for i in range(40):
                dosya_ad = f"{kanal}_parca_{i+1:03d}.mp4"
                # Çeşitli durumlar: biraz çakışma, biraz boşluk, normal bitişik
                if i % 5 == 1:
                    zaman += 0.8  # boşluk
                elif i % 5 == 3:
                    zaman -= 0.4  # çakışma
                parcalar.append(Parca(dosya_ad, zaman, 10.0))
                zaman += 10.0
            c = SanalZamanCizelgesi(parcalar)
            cizelgeler.append(c)

        # 3000 adet eşleme sorgusu koş
        for c in cizelgeler:
            adim = c.toplam_sure / 1000.0
            for k in range(1000):
                sanal_t = k * adim
                dosya, yerel = c.sanal_to_yerel(sanal_t)
                if dosya is not None:
                    _ = c.yerel_to_sanal(dosya, yerel)

        gecen_sure = time.perf_counter() - baslama
        self.assertLess(
            gecen_sure,
            1.0,
            f"3 kanal x 40 parça ve 3000 eşleme {gecen_sure:.3f} s sürdü (< 1.0 s bekleniyordu)",
        )

    def test_23_toplam_sure_tutarliligi(self):
        """Toplam sürenin tüm dilimlerin süreleri toplamına eşit olması."""
        parcalar = [
            Parca("p1.mp4", 100.0, 7.5),
            Parca("p2.mp4", 110.0, 5.0),  # 2.5 s boşluk
            Parca("p3.mp4", 114.0, 8.0),  # 1.0 s çakışma
        ]
        cizelge = SanalZamanCizelgesi(parcalar)

        dilim_sureleri_toplami = sum(d.sure for d in cizelge)
        self.assertAlmostEqual(cizelge.toplam_sure, dilim_sureleri_toplami)
        self.assertAlmostEqual(cizelge.toplam_sure_sn, dilim_sureleri_toplami)

    def test_24_fabrika_fonksiyonlari_ve_takma_adlar(self):
        """cizelge_olustur, parca_zinciri_olustur ve SanalZaman takma adları testi."""
        parcalar = [Parca("tek.mp4", 0.0, 5.0)]

        c1 = cizelge_olustur(parcalar)
        c2 = parca_zinciri_olustur(parcalar)
        c3 = zincir_olustur(parcalar)
        c4 = SanalZaman(parcalar)

        self.assertEqual(len(c1), len(c2))
        self.assertEqual(len(c2), len(c3))
        self.assertEqual(len(c3), len(c4))
        self.assertAlmostEqual(c1.toplam_sure, 5.0)

    def test_25_dogal_siralama_anahtari(self):
        """Doğal sıralama fonksiyonunun ders_2 < ders_10 kuralını sağlaması."""
        isimler = ["ders_10.mp4", "ders_2.mp4", "ders_1.mp4", "ders_20.mp4"]
        sirali = sorted(isimler, key=dogal_siralama_anahtari)
        self.assertEqual(sirali, ["ders_1.mp4", "ders_2.mp4", "ders_10.mp4", "ders_20.mp4"])

    def test_26_ayni_baslangic_zamani_dogal_zincirleme(self):
        """Tüm başlangıç zamanları eşit olduğunda parçalar silinmez, doğal ad sırasıyla zincirlenir."""
        parcalar = [
            Parca("b.mp4", 1000.0, 600.0),
            Parca("a.mp4", 1000.0, 600.0),
        ]
        cizelge = SanalZamanCizelgesi(parcalar)

        # İki parça da korunmalı
        self.assertEqual(len(cizelge), 2)
        self.assertAlmostEqual(cizelge.toplam_sure, 1200.0)
        self.assertEqual(cizelge[0].dosya, "a.mp4")
        self.assertEqual(cizelge[1].dosya, "b.mp4")
        self.assertAlmostEqual(cizelge[0].sanal_bas, 0.0)
        self.assertAlmostEqual(cizelge[0].sanal_son, 600.0)
        self.assertAlmostEqual(cizelge[1].sanal_bas, 600.0)
        self.assertAlmostEqual(cizelge[1].sanal_son, 1200.0)
        # Uyarı eklenmiş olmalı
        self.assertTrue(any("eşit" in u for u in cizelge.uyarilar))


if __name__ == "__main__":
    unittest.main()
