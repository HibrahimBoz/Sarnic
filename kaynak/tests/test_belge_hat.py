import json
from pathlib import Path
import shutil
import tracemalloc
from unittest.mock import patch

import pymupdf

from belge_destek import GeciciTest, SahteHavuz, pdf_yaz
from videohat.belge.hat import isle_belge, disa_yaz, sha256
from videohat.belge.cikar import cikar
from videohat.hatalar import IsHatasi
from videohat.__main__ import main


class HatTest(GeciciTest):
    def test_onbellek_kesilme_devam_pdf_tam(self):
        yol = pdf_yaz(self.kok / "uc.pdf", sayi=3, taranmis=True)
        depo = self.kok / "depo"
        kesilen = SahteHavuz(kes=3)
        with self.assertRaises(KeyboardInterrupt):
            isle_belge(yol, depo, self.ayar, kesilen)
        hedef = depo / "belgeler" / sha256(yol)[:2] / sha256(yol)
        self.assertFalse((hedef / "belge.json").exists())
        for no in (1, 2):
            veri = json.loads((hedef / "_ara" / f"sayfa_{no:05d}.json").read_text(encoding="utf-8"))
            self.assertEqual(set(veri) - {"uyarilar"}, {"no", "etiket", "kaynak", "metin", "guven", "satirlar", "gorsel_boyut", "yon", "dpi"})
        havuz = SahteHavuz()
        kayit = isle_belge(yol, depo, self.ayar, havuz)
        self.assertEqual(havuz.isler, [3])
        self.assertEqual(kayit.veri["ocr_sayfa"], 3)
        self.assertFalse((hedef / "_ara").exists())
        self.assertEqual((hedef / "metin.txt").read_text(encoding="utf-8").count("\f"), 2)
        self.assertFalse((hedef / "belge.json").read_bytes().startswith(b"\xef\xbb\xbf"))
        with pymupdf.open(hedef / "aranabilir.pdf") as d:
            for p in d:
                self.assertIn("frekansı", p.get_text())

    def test_sha_tek_islem_ve_surum_kontrolu(self):
        yol = self.kok / "a.txt"
        yol.write_text("Tek içerik", encoding="utf-8")
        diger = self.kok / "b.txt"
        shutil.copyfile(yol, diger)
        depo = self.kok / "depo"
        ilk = isle_belge(yol, depo, self.ayar)
        with patch("videohat.belge.hat.cikar", side_effect=AssertionError("TEKRAR İŞLENDİ")):
            ikinci = isle_belge(diger, depo, self.ayar)
        self.assertEqual(ilk.sha, ikinci.sha)
        self.assertEqual(len(list(depo.rglob("belge.json"))), 1)
        self.assertIsNone(ilk.veri["aranabilir_pdf"])
        eski = ilk.veri.copy()
        eski["surum"] = 0
        (ilk.hedef / "belge.json").write_text(json.dumps(eski), encoding="utf-8")
        self.assertEqual(isle_belge(yol, depo, self.ayar).veri["surum"], 1)

    def test_belge_json_yalniz_durum_tamam_ve_bicim_eslesmesi(self):
        # 5. bulgu: durum != 'tamam' iken belge.json yazılmaz; önbellekten okurken biçim eşleşmeli
        depo = self.kok / "depo"
        doc = self.kok / "a.doc"
        doc.write_bytes(b"bos")
        with patch("videohat.belge.ofis.yollar.libreoffice", return_value=None):
            kayit = isle_belge(doc, depo, self.ayar)
        self.assertEqual(kayit.veri["durum"], "desteklenmiyor")
        self.assertFalse((kayit.hedef / "belge.json").exists())

        # Aynı baytlı txt dosyasının önbellekten hatalı okunmaması (biçim eşleşmesi)
        txt = self.kok / "b.txt"
        txt.write_bytes(b"bos")
        kayit_txt = isle_belge(txt, depo, self.ayar)
        self.assertEqual(kayit_txt.veri["durum"], "tamam")
        self.assertTrue((kayit_txt.hedef / "belge.json").exists())

    def test_1000_sayfa_gorseller_bellekte_birikmez(self):
        yol = self.kok / "bin.pdf"
        with pymupdf.open() as d:
            for _ in range(1000):
                d.new_page(width=100, height=100)
            d.save(yol)
        tracemalloc.start()
        try:
            with patch.object(pymupdf.Page, "get_pixmap", side_effect=AssertionError("Ana süreçte görüntü üretildi")):
                s = cikar(yol, self.ayar, SahteHavuz(), self.kok / "ara")
            _, tepe = tracemalloc.get_traced_memory()
        finally:
            tracemalloc.stop()
        self.assertEqual(len(s.sayfalar), 1000)
        self.assertLess(tepe, 24 * 1024 * 1024)
        self.assertEqual(len(list((self.kok / "ara").glob("sayfa_*.json"))), 1000)

    def test_metinli_pdf_kopya_uretilmez_ve_duz_cikti(self):
        yol = pdf_yaz(self.kok / "metin.pdf")
        kayit = disa_yaz(yol, self.kok / "cikti", self.ayar)
        self.assertTrue(kayit.basarili)
        self.assertTrue((kayit.hedef / "belge.json").exists())
        self.assertFalse((kayit.hedef / "aranabilir.pdf").exists())
        self.assertFalse((kayit.hedef / "_ara").exists())

    def test_disa_yaz_hata_durumunda_ara_temizlenir(self):
        # 14. bulgu: disa_yaz hata fırlattığında _ara temizlenir
        cikti = self.kok / "cikti_hata"
        with patch("videohat.belge.hat.isle_belge", side_effect=IsHatasi("test hatası")):
            with self.assertRaises(IsHatasi):
                disa_yaz(self.kok / "a.txt", cikti, self.ayar)
        self.assertFalse((cikti / "_ara").exists())

    def test_izle_bos_ara_korunmaz_ve_yarim_temizlenir(self):
        # 14. bulgu: izle._geri_al içinde boş _ara koruma altına alınmaz
        from videohat import izle
        bitti = self.kok / "is" / "bitti"
        isleniyor = self.kok / "is" / "isleniyor"
        gelen = self.kok / "is" / "gelen"
        for d in (bitti, isleniyor, gelen):
            d.mkdir(parents=True, exist_ok=True)
        yetim_belge = bitti / "a_belge"
        yetim_belge.mkdir()
        (yetim_belge / "_ara").mkdir()  # Boş _ara
        (yetim_belge / ".yarim").touch()
        (isleniyor / "a.pdf").touch()
        izle._geri_al(isleniyor, gelen, bitti)
        self.assertFalse(yetim_belge.exists())

    def test_cli_belge_ve_hata_kodlari(self):
        yol = self.kok / "a.txt"
        yol.write_text("Türkçe ölçüm", encoding="utf-8")
        with patch("videohat.__main__._gunluk_kur"), patch("videohat.ayar.yukle", return_value=self.ayar):
            self.assertEqual(main(["belge", str(yol), "--cikis", str(self.kok / "cikti")]), 0)
            self.assertEqual(main(["belge", str(self.kok / "yok.txt")]), 1)
            self.assertEqual(main(["dizinle", "--bir-kez"]), 2)
        self.assertTrue((self.kok / "cikti" / "metin.txt").is_file())

    def test_izleyici_belge_yonu_video_hatti_cagrilmaz(self):
        from videohat import izle
        for ad in ("gelen", "isleniyor", "bitti", "hata"):
            (self.kok / "is" / ad).mkdir(parents=True)
        yol = self.kok / "is" / "gelen" / "metin.txt"
        yol.write_text("İzlenen belge", encoding="utf-8")
        self.ayar.izle.sabit_tur = 1
        with patch("videohat.izle.yollar.kok", return_value=self.kok), patch("videohat.izle.isle_tek") as video:
            self.assertEqual(izle.tek_tur(self.ayar, {}), 1)
        video.assert_not_called()
        cikis = self.kok / "is" / "bitti" / "metin_belge"
        self.assertTrue((cikis / "belge.json").is_file())
        self.assertTrue((cikis / "metin_1.txt").is_file())
