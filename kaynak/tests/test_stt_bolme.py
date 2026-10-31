import unittest
import sys
import tempfile
from pathlib import Path
from types import SimpleNamespace

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "app"))
from sarnicatolye.stt import (  # noqa: E402
    Segment, _adaylari_sec, _ikinci_gecis_bolgeleri, _istem_yap,
    _parca_siniri_tekrar_temizle, bolumleri_kes, filtrele_halusinasyonlar,
    bosluk_bul, bosluk_butcesi_sec, bosluk_ek_filtrele, cakisma_ayikla,
    enerji_aktif,
)
from sarnicatolye.ayar import yukle  # noqa: E402


def kelime(start, end, text, probability=0.99):
    return SimpleNamespace(start=start, end=end, word=f" {text}", probability=probability)


def bolum(start, end, text="", words=None):
    return SimpleNamespace(start=start, end=end, text=text, words=words)


class SttBolmeTesti(unittest.TestCase):
    def test_uzun_segment_sessizligi_kelime_zamanindan_atar(self):
        words = [kelime(510 + i * 2, 511 + i * 2, "konuşma") for i in range(17)]
        sonuc = bolumleri_kes([bolum(4.9, 420.3, "uzun", words)])
        self.assertTrue(sonuc)
        self.assertGreaterEqual(sonuc[0].bas, 510)
        self.assertTrue(all(s.son - s.bas <= 7 for s in sonuc))

    def test_yedi_saniye_ve_seksen_dort_karakter_sinirinda_boler(self):
        words = [kelime(i * 0.5, i * 0.5 + 0.4, "abcdefgh") for i in range(22)]
        sonuc = bolumleri_kes([bolum(0, 20, words=words)])
        self.assertGreater(len(sonuc), 1)
        self.assertTrue(all(s.son - s.bas <= 7 for s in sonuc))
        self.assertTrue(all(len(s.metin) <= 84 for s in sonuc))

    def test_bir_saniyeden_buyuk_bosluk_boler(self):
        words = [kelime(0, 0.5, "Birinci"), kelime(1.6, 2.0, "ikinci.")]
        sonuc = bolumleri_kes([bolum(0, 2, words=words)])
        self.assertEqual(len(sonuc), 2)
        self.assertEqual(sonuc[0].son, 1.0)
        self.assertEqual(sonuc[1].bas, 1.6)

    def test_uc_ardisik_ayni_metinden_ilkini_tutar(self):
        sonuc = filtrele_halusinasyonlar([
            Segment(0, 1, "Aynı metin"), Segment(1, 2, "Aynı metin"),
            Segment(2, 3, "Aynı metin"),
        ])
        self.assertEqual(sonuc, [Segment(0, 1, "Aynı metin")])

    def test_bilinen_halisinasyon_filtrelenir(self):
        metinler = [
            "Altyazı M.K.",
            "Altyazılar M.K.",
            "İzlediğiniz için teşekkür ederim.",
            "İZLEDİĞİNİZ İÇİN TEŞEKKÜRLER",
            "Altyazı ekleyen ve yorumladığı için teşekkürler",
            "altyazı ekleyen ve yorumladığı için",
            "ekleyen ve yorumladığı",
        ]
        self.assertEqual(
            filtrele_halusinasyonlar([Segment(0, 2, metin) for metin in metinler]),
            [],
        )

    def test_parcalara_bolunmus_altyazi_halusinasyonu_atilir(self):
        sonuc = filtrele_halusinasyonlar([
            Segment(10, 10.4, "Altyazı"),
            Segment(10.4, 11.2, "ekleyen ve yorumladığı"),
            Segment(11.2, 11.8, "için teşekkürler."),
            Segment(12, 13, "gerçek konuşma"),
        ])
        self.assertEqual([s.metin for s in sonuc], ["gerçek konuşma"])

    def test_ingilizce_sarki_filtresi_turkce_metinleri_korur(self):
        self.assertEqual(
            filtrele_halusinasyonlar([
                Segment(0, 2, "Let me see you bounce, let me see you bounce, ooo"),
            ]),
            [],
        )
        turkce = [
            Segment(0, 2, "bugun ders yok"),
            Segment(2, 4, "Bugün ders yok"),
            Segment(4, 6, "Let me see bir tane"),
        ]
        self.assertEqual(filtrele_halusinasyonlar(turkce), turkce)

    def test_dusuk_guvenli_kisa_segment_elenir(self):
        dusuk = [kelime(0, 1, "kutu,", 0.2), kelime(1, 2, "kutu", 0.2),
                 kelime(2, 3, "söyle", 0.2), kelime(3, 3.5, "bir", 0.2),
                 kelime(3.5, 4, "tane", 0.2),
                 kelime(5.1, 6.1, "yüksek", 0.9),
                 kelime(6.1, 7.1, "güven", 0.9),
                 kelime(8.2, 8.7, ".sorularlaislamiyet", 0.2),
                 kelime(8.7, 9.2, ".com", 0.2),
                 kelime(10.3, 10.8, "Erişim", 0.2),
                 kelime(10.8, 11.3, "www", 0.2),
                 kelime(20, 25, "güvensiz", 0.2)]
        sonuc = bolumleri_kes([bolum(0, 25, words=dusuk)])
        self.assertFalse(any(s.guven == 0.2 for s in sonuc))
        self.assertIn("yüksek güven", [s.metin for s in sonuc])

    def test_orta_guven_sadece_kisa_segmentte_elenir(self):
        kisa = [kelime(0, 1, "kısa", 0.4)]
        uzun = [kelime(5, 6, "bu", 0.4), kelime(6, 7, "uzun", 0.4), kelime(7, 8, "bir", 0.4),
                 kelime(8, 9, "cümle", 0.4), kelime(9, 10, "midir", 0.4)]
        sonuc = bolumleri_kes([bolum(0, 1, words=kisa), bolum(5, 10, words=uzun)])
        self.assertEqual([s.metin for s in sonuc], ["bu uzun bir cümle midir"])

    def test_sifira_yakin_tek_kelime_atilir(self):
        sonuc = bolumleri_kes([bolum(0, 0.2, words=[kelime(0, 0.2, "değerli")])])
        self.assertEqual(sonuc, [])

    def test_eski_stt_ayarlari_guven_esiklerini_varsayilan_alir(self):
        with tempfile.TemporaryDirectory() as gecici:
            kok = Path(gecici)
            (kok / "ayar.toml").write_text("[stt]\nbeam_size = 3\n", encoding="utf-8")
            ayar = yukle(kok)
        self.assertEqual(ayar.stt.guven_esik, 0.45)
        self.assertEqual(ayar.stt.guven_kisa_esik, 0.30)
        self.assertTrue(ayar.stt.baglam_tasi)
        self.assertTrue(ayar.stt.ikinci_gecis)
        self.assertEqual(ayar.stt.ikinci_gecis_esik, 0.60)
        self.assertEqual(ayar.stt.ikinci_gecis_en_cok_oran, 0.25)

    def test_kelimesiz_uzun_segment_kisaltilir(self):
        sonuc = bolumleri_kes([bolum(2, 40, "abcdefghij")])
        self.assertEqual(sonuc, [Segment(2, 3.0, "abcdefghij")])

    def test_parca_ofseti_sirayi_ve_cakismayi_duzeltir(self):
        words = [kelime(0, 1, "ikinci parça")]
        sonuc = bolumleri_kes([bolum(0, 1, words=words)], ofset=1200, onceki_son=1200.5)
        self.assertEqual(sonuc, [Segment(1200.5, 1201.5, "ikinci parça")])

    def test_parca_siniri_turkce_case_ve_ek_tekrari_birlestirir(self):
        sonuc = _parca_siniri_tekrar_temizle(
            [Segment(1198, 1200, "RADAR İZİ"), Segment(1200, 1202, "radar izi")],
            [1200],
        )
        self.assertEqual(len(sonuc), 1)
        self.assertEqual(sonuc[0].metin, "RADAR İZİ")
        ek = _parca_siniri_tekrar_temizle(
            [Segment(1198, 1200, "Sinyali tekrar"), Segment(1200, 1202, "tekrar")],
            [1200],
        )
        self.assertEqual([s.metin for s in ek], ["Sinyali tekrar"])

    def test_kisa_altyazi_bir_saniyeye_uzar_ama_sonrakine_tasmaz(self):
        sonuc = bolumleri_kes([
            bolum(0, 0.5, words=[kelime(0, 0.5, "çıkar.")]),
            bolum(0.7, 1.2, words=[kelime(0.7, 1.2, "sonraki")]),
        ])
        self.assertEqual(sonuc[0].son, 0.7)
        self.assertLessEqual(sonuc[0].son, sonuc[1].bas)
        tek = bolumleri_kes([bolum(8, 8.5, words=[kelime(8, 8.5, "çıkar.")])])
        self.assertEqual(tek[0].son, 9.0)

    def test_dosya_sonundaki_kisa_altyazi_toplam_sureyi_asmiyor(self):
        sonuc = bolumleri_kes(
            [bolum(8.3, 8.7, words=[kelime(8.3, 8.7, "son.")])],
            toplam_sn=8.8,
        )
        self.assertEqual(sonuc[0].son, 8.8)

    def test_ikinci_gecis_bolgesi_guven_butcesi_ve_birlestirme(self):
        segments = [
            Segment(10, 14, "düşük güvenli alan", 0.4),
            Segment(15, 18, "ikinci düşük alan", 0.5),
            Segment(30, 34, "yüksek güven", 0.9),
        ]
        bolgeler = _ikinci_gecis_bolgeleri(segments, 100, 0.6, 0.25)
        self.assertEqual(bolgeler, [(9, 19)])
        self.assertLessEqual(sum(b - a for a, b in bolgeler), 25)

    def test_ikinci_gecis_butceye_sigmayan_bolgeyi_kesmez(self):
        segments = [
            Segment(1, 13, "en düşük güven", 0.1),
            Segment(20, 27, "bütçeye sığan", 0.2),
        ]
        bolgeler = _ikinci_gecis_bolgeleri(segments, 100, 0.6, 0.12)
        self.assertEqual(bolgeler, [(19, 28)])
        self.assertNotIn((0, 12), bolgeler)

    def test_aday_secimi_guven_farki_kucukse_ilk_gecisi_korur(self):
        ilk = [Segment(1, 2, "ilk geçiş", 0.70)]
        temiz = [Segment(1, 2, "temiz yeniden", 0.72)]
        self.assertEqual(_adaylari_sec(ilk, temiz, []), ilk)
        daha_iyi = [Segment(1, 2, "daha güvenli", 0.80)]
        self.assertEqual(_adaylari_sec(ilk, daha_iyi, []), daha_iyi)
        halusinasyon = [Segment(1, 4, "abone ol", 0.99)]
        self.assertEqual(_adaylari_sec(halusinasyon, daha_iyi, []), daha_iyi)
        self.assertEqual(_adaylari_sec(halusinasyon, halusinasyon, []), [])

    def test_ilk_prompt_terimleri_ve_onceki_cumleyle_olusturulur(self):
        self.assertEqual(_istem_yap("", "radar, karıştırma"), "radar, karıştırma")
        self.assertEqual(
            _istem_yap("eski cümle. Son cümle burada.", "radar"),
            "Son cümle burada. radar",
        )

    def test_enerji_aktif_sentetik_dizi(self):
        rms = np.array([-70.0, -70.0, -65.0, -60.0, -56.0, -54.0, -45.0, -30.0, -20.0, -10.0])
        aktif = enerji_aktif(rms, bosluk_enerji_db=8.0)
        self.assertFalse(aktif[3])
        self.assertFalse(aktif[4])
        self.assertTrue(aktif[5])
        self.assertTrue(aktif[6])
        self.assertTrue(aktif[7])
        self.assertEqual(len(enerji_aktif(np.array([]))), 0)

    def test_bosluk_bul_sessiz_ve_aktif_bosluk(self):
        aktif = np.zeros(100, dtype=bool)
        aktif[10:30] = True
        aktif[60:65] = True
        segmentler = [Segment(40.0, 50.0, "konuşma")]
        bosluklar = bosluk_bul(segmentler, aktif, toplam_sn=100.0, bosluk_min_sn=30.0, bosluk_aktif_orani=0.40)
        self.assertEqual(bosluklar, [(0.0, 40.0)])

    def test_bosluk_bul_dosya_basi_ve_sonu(self):
        aktif = np.ones(160, dtype=bool)
        segmentler = [Segment(35.0, 45.0, "bir"), Segment(85.0, 95.0, "iki")]
        bosluklar = bosluk_bul(segmentler, aktif, toplam_sn=160.0, bosluk_min_sn=30.0, bosluk_aktif_orani=0.40)
        self.assertEqual(bosluklar, [(0.0, 35.0), (45.0, 85.0), (95.0, 160.0)])

    def test_bosluk_bul_kisa_bosluk_atlanir(self):
        aktif = np.ones(60, dtype=bool)
        segmentler = [Segment(20.0, 45.0, "konuşma")]
        bosluklar = bosluk_bul(segmentler, aktif, toplam_sn=60.0, bosluk_min_sn=30.0)
        self.assertEqual(bosluklar, [])

    def test_ek_filtre_muzik_simgeleri_ve_etiketleri(self):
        adaylar = [
            Segment(0.0, 2.0, "🎵", 0.90),
            Segment(2.0, 4.0, "♪ ♪ ♪", 0.90),
            Segment(4.0, 6.0, "♫", 0.90),
            Segment(6.0, 8.0, "[Müzik]", 0.90),
            Segment(8.0, 10.0, "(müzik)", 0.90),
            Segment(10.0, 12.0, "Müzik", 0.90),
            Segment(12.0, 14.0, "alkış", 0.90),
            Segment(14.0, 16.0, "[Alkış]", 0.90),
            Segment(16.0, 18.0, "Altyazı M.K.", 0.90),
            Segment(18.0, 20.0, "Bye bye", 0.90),
            Segment(20.0, 25.0, "Çalışarak öğreniriz, her güçlüğü yeneriz.", 0.85),
        ]
        sonuc = bosluk_ek_filtrele(adaylar)
        self.assertEqual(len(sonuc), 1)
        self.assertEqual(sonuc[0].metin, "Çalışarak öğreniriz, her güçlüğü yeneriz.")

    def test_ek_filtre_dongu_atlanir(self):
        dongu = [
            Segment(0.0, 1.0, "Tekrar eden metin", 0.80),
            Segment(1.0, 2.0, "Tekrar eden metin", 0.80),
            Segment(2.0, 3.0, "Tekrar eden metin", 0.80),
            Segment(4.0, 6.0, "Gerçek konuşma", 0.85),
        ]
        sonuc = bosluk_ek_filtrele(dongu)
        self.assertEqual([s.metin for s in sonuc], ["Gerçek konuşma"])

        iki_tekrar = [
            Segment(0.0, 1.0, "Aynı", 0.80),
            Segment(1.0, 2.0, "Aynı", 0.80),
        ]
        self.assertEqual(len(bosluk_ek_filtrele(iki_tekrar)), 2)

    def test_ek_filtre_dusuk_guven_atlanir(self):
        adaylar = [
            Segment(0.0, 2.0, "düşük güvenli altyazı", 0.35),
            Segment(2.0, 4.0, "yüksek güvenli altyazı", 0.65),
        ]
        sonuc = bosluk_ek_filtrele(adaylar, guven_esik=0.40)
        self.assertEqual([s.metin for s in sonuc], ["yüksek güvenli altyazı"])

    def test_cakisma_ayiklama_mevcut_korunur(self):
        mevcut = [
            Segment(10.0, 20.0, "Mevcut 1"),
            Segment(50.0, 60.0, "Mevcut 2"),
        ]
        yeni = [
            Segment(5.0, 12.0, "Çakışan aday 1"),
            Segment(25.0, 35.0, "Temiz aday 1"),
            Segment(55.0, 65.0, "Çakışan aday 2"),
            Segment(70.0, 80.0, "Temiz aday 2"),
        ]
        sonuc = cakisma_ayikla(mevcut, yeni)
        self.assertEqual(
            [s.metin for s in sonuc],
            ["Mevcut 1", "Temiz aday 1", "Mevcut 2", "Temiz aday 2"],
        )

    def test_bosluk_butce_en_uzun_bosluklar_once(self):
        bosluklar = [
            (10.0, 40.0),
            (50.0, 110.0),
            (120.0, 170.0),
        ]
        secilen = bosluk_butcesi_sec(bosluklar, toplam_sn=200.0, en_cok_oran=0.50)
        self.assertEqual(secilen, [(10.0, 40.0), (50.0, 110.0)])

    def test_eski_ayar_toml_bosluk_doldur_varsayilanlari_alir(self):
        with tempfile.TemporaryDirectory() as gecici:
            kok = Path(gecici)
            (kok / "ayar.toml").write_text("[stt]\nbeam_size = 5\n", encoding="utf-8")
            ayar = yukle(kok)
        self.assertTrue(ayar.stt.bosluk_doldur)
        self.assertEqual(ayar.stt.bosluk_min_sn, 30.0)
        self.assertEqual(ayar.stt.bosluk_aktif_orani, 0.40)
        self.assertEqual(ayar.stt.bosluk_enerji_db, 3.0)
        self.assertEqual(ayar.stt.bosluk_en_cok_oran, 1.0)

    def test_karakter_tekrari_halusinasyonu_atlanir(self):
        adaylar = [
            Segment(0.0, 2.0, "UUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUU UUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUUU"),
            Segment(2.0, 4.0, "ÖÖÖÖÖÖ ÖL AKŞIDIR!!"),
            Segment(4.0, 6.0, "aaaaaa konuşma"),
            Segment(6.0, 8.0, "saat iki yönünde"),
        ]
        sonuc = filtrele_halusinasyonlar(adaylar)
        self.assertEqual([s.metin for s in sonuc], ["saat iki yönünde"])
        sonuc_bosluk = bosluk_ek_filtrele(adaylar)
        self.assertEqual([s.metin for s in sonuc_bosluk], ["saat iki yönünde"])

    def test_buyuk_harfli_uydurma_cumle_atlanir(self):
        uydurmalar = [
            Segment(0.0, 3.0, "HAKEM GELSEN İÇERE, PİSLE BAKMASIN GEREĞE..."),
            Segment(3.0, 6.0, "GİDAR SİKORUYORLAR SEVELİ KOPUTLAR SORMUYOR"),
            Segment(6.0, 9.0, "KERAL VE HİCA 'LA CEVAP VERDİ"),
            Segment(9.0, 12.0, "HEMET ÇEVİRİM SİROGÜ ÜZÜNÜ DETİK İÇİN KOPUTLARIM"),
            Segment(12.0, 15.0, "SOSYAL MÜZİĞİ"),
            Segment(15.0, 18.0, "Gerçek Türkçe cümle burada devam ediyor."),
        ]
        sonuc = filtrele_halusinasyonlar(uydurmalar)
        self.assertEqual([s.metin for s in sonuc], ["Gerçek Türkçe cümle burada devam ediyor."])
        sonuc_bosluk = bosluk_ek_filtrele(uydurmalar)
        self.assertEqual([s.metin for s in sonuc_bosluk], ["Gerçek Türkçe cümle burada devam ediyor."])

    def test_kisa_buyuk_harfli_teknik_satirlar_korunur(self):
        teknik = [
            Segment(0.0, 2.0, "ECM"),
            Segment(2.0, 4.0, "RADAR"),
            Segment(4.0, 6.0, "ÖN HAT"),
            Segment(6.0, 8.0, "SES TEST"),
        ]
        sonuc = filtrele_halusinasyonlar(teknik)
        self.assertEqual([s.metin for s in sonuc], ["ECM", "RADAR", "ÖN HAT", "SES TEST"])
        sonuc_bosluk = bosluk_ek_filtrele(teknik)
        self.assertEqual([s.metin for s in sonuc_bosluk], ["ECM", "RADAR", "ÖN HAT", "SES TEST"])

    def test_abone_olmayi_unutmayin_varyantlari_atlanir(self):
        kalip_adaylari = [
            Segment(0.0, 2.0, "ABONE OLMAYI UNUTMAYINIZ"),
            Segment(2.0, 4.0, "ABONE OLMAYI UNUTMAYIN!"),
            Segment(4.0, 6.0, "OLMAYI UNUTMAYINIZ!"),
            Segment(6.0, 8.0, "abone olmayı unutmayın"),
            Segment(8.0, 10.0, "kanala abone ol"),
            Segment(10.0, 12.0, "videoyu beğenmeyi unutmayın"),
            Segment(12.0, 14.0, "beğenmeyi unutmayın"),
            Segment(14.0, 16.0, "videoyu begenmeyi unutmayin"),
            Segment(16.0, 18.0, "begenmeyi unutmayin"),
            Segment(18.0, 20.0, "sosyal müziği"),
            Segment(20.0, 22.0, "altyazı m k"),
            Segment(22.0, 24.0, "izlediğiniz için teşekkürler"),
            Segment(24.0, 26.0, "Burada okul gösterisi devam ediyor."),
        ]
        sonuc = filtrele_halusinasyonlar(kalip_adaylari)
        self.assertEqual([s.metin for s in sonuc], ["Burada okul gösterisi devam ediyor."])
        sonuc_bosluk = bosluk_ek_filtrele(kalip_adaylari)
        self.assertEqual([s.metin for s in sonuc_bosluk], ["Burada okul gösterisi devam ediyor."])

    def test_uc_kez_tekrar_eden_kisa_metin_tumden_atlanir(self):
        uzun_cumle = "Bu cümle beş kelimeden daha uzun bir cümledir."
        segmentler = [
            Segment(0.0, 2.0, "kısa metin"),
            Segment(5.0, 7.0, uzun_cumle),
            Segment(10.0, 12.0, "farklı bir cümle"),
            Segment(15.0, 17.0, "kısa metin"),
            Segment(20.0, 22.0, uzun_cumle),
            Segment(25.0, 27.0, "bir başka normal cümle"),
            Segment(30.0, 32.0, "kısa metin"),
            Segment(35.0, 37.0, uzun_cumle),
        ]
        sonuc = filtrele_halusinasyonlar(segmentler)
        self.assertEqual(
            [s.metin for s in sonuc],
            [uzun_cumle, "farklı bir cümle", uzun_cumle, "bir başka normal cümle", uzun_cumle],
        )

        sonuc_bosluk = bosluk_ek_filtrele(segmentler)
        self.assertEqual(
            [s.metin for s in sonuc_bosluk],
            [uzun_cumle, "farklı bir cümle", uzun_cumle, "bir başka normal cümle", uzun_cumle],
        )

    def test_normal_turkce_cumleler_korunur(self):
        cumleler = [
            Segment(0.0, 3.0, "Bugün okulumuzda harika bir gösteri izledik."),
            Segment(4.0, 7.0, "Öğrenciler sahnede çok başarılı bir performans sergiledi."),
            Segment(8.0, 10.0, "Tebrikler çocuklar!"),
        ]
        sonuc = filtrele_halusinasyonlar(cumleler)
        self.assertEqual([s.metin for s in sonuc], [s.metin for s in cumleler])
        sonuc_bosluk = bosluk_ek_filtrele(cumleler)
        self.assertEqual([s.metin for s in sonuc_bosluk], [s.metin for s in cumleler])


if __name__ == "__main__":
    unittest.main()
