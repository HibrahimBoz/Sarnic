from belge_destek import GeciciTest
from videohat.belge.sozluk import Sozluk
from videohat.belge.model import OcrSatir
from videohat.belge.sira import sirala, birlestir, ortalama


class SozlukTest(GeciciTest):
    def test_guven_turkce_ciftler_buyukluk_kisaltma_sayi(self):
        s = Sozluk()
        for kelime in ("yapılmalıdır", "güvenlik", "ölçümü", "ışık", "işlem", "prf", "ş1"):
            s.ekle(kelime)
        self.assertEqual(s.duzelt("yapilmalidir", .8), "yapılmalıdır")
        self.assertEqual(s.duzelt("Yapilmalidir", .8), "Yapılmalıdır")
        self.assertEqual(s.duzelt("yapilmalidir", .99), "yapilmalidir")
        self.assertEqual(s.duzelt("yapilmalidir", .96), "yapılmalıdır")  # ölçüldü: hatalı satır güveni 0,96
        self.assertEqual(s.duzelt("iletișim", .9), "iletişim")
        u = Sozluk()
        for kelime in ("komutan", "alın", "geliyor"):
            u.ekle(kelime)
        self.assertEqual(u.duzelt("Komutanlik alinmadan", .96), "Komutanlık alınmadan")  # kök + ünlü uyumu
        self.assertEqual(u.duzelt("geliyorki", .96), "geliyorki")  # uyumsuz ek: dokunulmaz
        k = Sozluk(unlu_uyumu=False)
        k.ekle("komutan")
        self.assertEqual(k.duzelt("Komutanlik", .96), "Komutanlik")
        self.assertEqual(s.duzelt("GiZLiLiK ve ECM", .9), "GİZLİLİK ve ECM")
        self.assertEqual(s.duzelt("PRF s1 IŞIK X", .5), "PRF s1 IŞIK X")
        self.assertEqual(s.duzelt("Isik islem olcumu", .5), "Işık işlem ölçümü")
        self.assertEqual(s.duzelt("bilinmeyen guvenlik", .8), "bilinmeyen güvenlik")

    def test_birebir_yazim_ve_en_sik_sozluk_terimler(self):
        yol = self.kok / "s.txt"
        yol.write_text("sık 10\nşık 20\nâlem 30\n", encoding="utf-8")
        terim = self.kok / "terimler.txt"
        terim.write_text("[istem]\nyapılmalıdır\n[duzeltme]\nyanlış => ölçüm\n", encoding="utf-8")
        s = Sozluk(yol, terim)
        self.assertEqual(s.duzelt("sık", .4), "sık")
        self.assertEqual(s.duzelt("sik", .4), "şık")
        self.assertEqual(s.duzelt("alem", .4), "alem")  # â/a izinli karışma değil.
        self.assertEqual(s.duzelt("yapilmalidir olcum", .5), "yapılmalıdır ölçüm")
        self.assertEqual(s.oran("ölçüm bilinmez"), .5)
        self.assertGreater(s.puan("ölçüm", .7), s.puan("olcum", .8))

    def test_iki_sutun_baslik_ve_satir_ici_sira(self):
        kutular = [OcrSatir("BAŞLIK", 1, (10, 0, 990, 30))]
        for x, ad in ((20, "sol"), (560, "sağ")):
            for i in range(3):
                kutular.append(OcrSatir(f"{ad}{i}", .9, (x, 70 + i * 30, x + 350, 90 + i * 30)))
        sonuc = sirala(list(reversed(kutular)), (1000, 200))
        self.assertEqual([s.metin for s in sonuc], ["BAŞLIK", "sol0", "sol1", "sol2", "sağ0", "sağ1", "sağ2"])
        self.assertIn("sol2\nsağ0", birlestir(sonuc))
        kutular[0].kutu = (450, 40, 600, 60)  # Dar, ortalanmış başlık da oluğu kapatır.
        self.assertEqual([s.metin for s in sirala(kutular[::-1], (1000, 200))],
                         ["BAŞLIK", "sol0", "sol1", "sol2", "sağ0", "sağ1", "sağ2"])
        ayni = [OcrSatir("ikinci", .8, (60, 10, 100, 30)), OcrSatir("ilk", 1, (10, 10, 50, 30))]
        self.assertEqual(birlestir(sirala(ayni, (100, 40))), "ilk ikinci")

    def test_girinti_sutun_degil_ve_agirlikli_guven(self):
        s = [OcrSatir("a", .5, (200, 0, 700, 20)), OcrSatir("uzun metin", 1, (10, 30, 900, 50)),
             OcrSatir("son", 1, (30, 100, 900, 120))]
        self.assertEqual(sirala(s[::-1], (1000, 150)), s)
        self.assertIn("\n\nson", birlestir(s))
        self.assertAlmostEqual(ortalama(s[:2]), 10.5 / 11)
