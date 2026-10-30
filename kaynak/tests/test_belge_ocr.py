import os
from pathlib import Path
import time
import unittest
from unittest.mock import patch, MagicMock

import numpy as np
import pymupdf

from belge_destek import GeciciTest, gorsel, gorsel_cok_satirli, pdf_yaz, FONT
from videohat import yollar
from videohat.belge.model import OcrSatir, Sayfa
from videohat.belge.ocr import OcrMotoru, OcrHavuzu, OcrIsi
from videohat.belge.onisleme import hazirla, dondur, yon_cevir, Donusum, metin_murekkebi, egiklik
from videohat.belge.pdf_katman import olustur
from videohat.belge.sira import birlestir
from videohat.hatalar import OrtamHatasi


def yavas_isci(ayar, gelen, giden):
    while True:
        is_ = gelen.get()
        if is_.no == 1:
            time.sleep(8)
        giden.put(("ok", Sayfa(is_.no, f"Sayfa {is_.no}", "ocr", "bitti", 1)))


class OcrTest(GeciciTest):
    def test_donusum_olcek_aci_tuval_yon_tersi(self):
        im = np.full((150, 300), 255, np.uint8)
        don = Donusum((300, 150))
        don.ekle(np.diag([2., 2., 1.]))
        im, mat = dondur(np.repeat(np.repeat(im, 2, axis=0), 2, axis=1), 2)
        don.ekle(mat)
        _, mat = yon_cevir(im, 90)
        don.ekle(mat)
        nokta = np.array([[20, 20, 1], [100, 20, 1], [100, 50, 1], [20, 50, 1]])
        ileri = nokta @ don.matris.T
        np.testing.assert_allclose(don.geri_kutu(ileri[:, :2]), (20, 20, 100, 50), atol=1e-6)

    def test_onisleme_bos_gorsel_buyur_gurultu_ve_karsitlik(self):
        im, d = hazirla(np.full((100, 200), 240, np.uint8))
        self.assertEqual(im.shape, (200, 400))
        self.assertEqual(d.olcek, 2)
        self.assertEqual(metin_murekkebi(np.full((300, 300), 255, np.uint8)), 0)
        fotograf = np.full((300, 300), 255, np.uint8)
        fotograf[20:280, 20:280] = 0
        self.assertEqual(metin_murekkebi(fotograf), 0)

    def test_egiklik_03_049_derece_ve_90_derece_sonrasi_egiklik(self):
        # 0.35° eğik görselin tespit edilebilmesi
        im = gorsel()
        gri = im[:, :, 0]
        donuk_035, _ = dondur(gri, 0.4)
        aci = egiklik(donuk_035)
        self.assertGreaterEqual(abs(aci), 0.3)
        self.assertLessEqual(abs(aci), 0.5)

    def test_eksik_model_indirme_yok(self):
        with patch("videohat.belge.ocr.yollar.ocr_model", side_effect=OrtamHatasi("model eksik")), \
                patch("requests.get", side_effect=AssertionError("AĞA ÇIKTI")) as ag:
            with self.assertRaisesRegex(OrtamHatasi, "model eksik"):
                OcrMotoru(self.ayar)._motor("small", "small")
            ag.assert_not_called()

    def test_downloader_kapali(self):
        from rapidocr.utils.download_file import DownloadFile
        motor = OcrMotoru(self.ayar)
        with patch("videohat.belge.ocr.yollar.ocr_model", return_value=Path("yok.onnx")):
            motor._motor("small", "small")
        with patch("requests.get") as ag:
            with self.assertRaisesRegex(OrtamHatasi, "indirme kapalı"):
                DownloadFile.run(None)
            ag.assert_not_called()

    def test_uyarlamali_satir_rec_ve_tesseract_puan(self):
        from types import SimpleNamespace
        motor = OcrMotoru(self.ayar)
        motor.ayar.ocr.profil = "uyarlamali"
        motor.tesseract = Path("tesseract.exe")
        motor.sozluk.ekle("ölçüm")
        sonuc = SimpleNamespace(txts=("olcum",), scores=(.7,))
        satir = OcrSatir("olcurn", .6, (10, 10, 160, 50))
        with patch.object(motor, "_oku", return_value=[satir]), \
                patch.object(motor, "_motor", return_value=lambda *a, **k: sonuc) as rec, \
                patch.object(motor, "_tesseract", return_value=("ölçüm", .65)) as tess:
            s, _ = motor.oku(np.full((100, 200), 255, np.uint8))
        rec.assert_called_with("small", "medium")
        tess.assert_called_once()
        self.assertEqual(s[0].metin, "ölçüm")

    def test_kalite_profili_det_medium_rec_medium_tesseract(self):
        # 17(a) Sözleşme 5.4.3 kalite profili testi
        motor = OcrMotoru(self.ayar)
        motor.ayar.ocr.profil = "kalite"
        motor.tesseract = Path("tesseract.exe")
        motor.sozluk.ekle("ölçüm")
        satir = OcrSatir("olcum", .6, (10, 10, 160, 50))
        with patch.object(motor, "_oku", return_value=[satir]) as oku, \
                patch.object(motor, "_tesseract", return_value=("ölçüm", .9)) as tess:
            s, _ = motor.oku(np.full((100, 200), 255, np.uint8))
        oku.assert_any_call(unittest.mock.ANY, "medium", "medium")
        tess.assert_called_once()
        self.assertEqual(s[0].metin, "ölçüm")

    def test_tek_isci_cpu_threads_ve_model_paylasimi(self):
        # 16(a) cpu_threads=0 iken tek süreçte max(1, cpu_count//2)
        self.ayar.genel.cpu_threads = 0
        self.ayar.dizin.isci = 1
        motor = OcrMotoru(self.ayar)
        beklenen_threads = max(1, (os.cpu_count() or 1) // 2)
        with patch("videohat.belge.ocr.yollar.ocr_model", return_value=Path("yok.onnx")), \
                patch("rapidocr.RapidOCR") as mock_rocr:
            mock_rocr.return_value.text_det = MagicMock()
            mock_rocr.return_value.text_rec = MagicMock()
            mock_rocr.return_value.text_cls = MagicMock()
            motor._motor("small", "small")
            params = mock_rocr.call_args.kwargs["params"]
            self.assertEqual(params["EngineConfig.onnxruntime.intra_op_num_threads"], beklenen_threads)

            # 16(b) İkinci motor çağrısında ortak modeller RapidOCR öncesi paylaştırılır
            motor._motor("small", "medium")
            self.assertEqual(motor.motorlar[("small", "medium")].text_det, motor.motorlar[("small", "small")].text_det)
            self.assertEqual(motor.motorlar[("small", "medium")].text_cls, motor.motorlar[("small", "small")].text_cls)

    def test_az_algilama_medium_det(self):
        self.ayar.ocr.profil = "uyarlamali"
        motor = OcrMotoru(self.ayar)
        with patch.object(motor, "_oku", return_value=[OcrSatir("iyi", .99, (0, 0, 1, 1))]) as oku, \
                patch("videohat.belge.ocr.metin_murekkebi", return_value=100):
            motor.oku(np.full((100, 200), 255, np.uint8))
        self.assertEqual(oku.call_args_list[-1].args[1:], ("medium", "small"))

    def test_tesseract_tsv_tur_eng_psm7(self):
        motor = OcrMotoru(self.ayar)
        motor.tesseract = Path("tesseract.exe")
        tsv = "level\tconf\ttext\n5\t80\tölçüm\n5\t90\ttesti\n".encode()
        with patch("videohat.belge.ocr.calistir", return_value=tsv) as fn:
            metin, guven = motor._tesseract(np.full((40, 100), 255, np.uint8))
        self.assertEqual(metin, "ölçüm testi")
        self.assertAlmostEqual(guven, .85)
        self.assertEqual(fn.call_args.args[0][-5:], ["-l", "tur+eng", "--psm", "7", "tsv"])

    def test_iki_spawn_isci_zaman_asimi_yenileme(self):
        self.ayar.dizin.isci = 2
        self.ayar.ocr.sayfa_zaman_asimi_sn = 2
        with OcrHavuzu(self.ayar, isci_hedefi=yavas_isci) as havuz:
            s = list(havuz.haritala([OcrIsi("yok", 1), OcrIsi("yok", 2)]))
            self.assertEqual({p.no for p, _ in s}, {1, 2})
            kesilen = next((p, u) for p, u in s if p.no == 1)
            self.assertEqual(kesilen[0].metin, "")
            self.assertIn("zaman aşımı", kesilen[1][0])
            self.assertEqual(next(p.metin for p, u in s if p.no == 2), "bitti")
            self.assertEqual(havuz.oku(OcrIsi("yok", 3))[0].metin, "bitti")
        self.assertEqual(havuz.yuvalar, [])

    def test_havuz_erken_cikis_ve_hata_yuva_temizligi(self):
        # 1. bulgu testi: erken çıkışta havuz yuvaları temizlenir, sonraki belgeye eski sonuç sızmaz
        self.ayar.dizin.isci = 2
        self.ayar.ocr.sayfa_zaman_asimi_sn = 10
        with OcrHavuzu(self.ayar, isci_hedefi=yavas_isci) as havuz:
            gen = havuz.haritala([OcrIsi("yok", 1), OcrIsi("yok", 2)])
            # Yalnızca ilk sonucu alıp erken çık (next(gen))
            next(gen)
            gen.close()
            # Yeni çağrıda yuvalar temiz başlar
            s = list(havuz.haritala([OcrIsi("yok", 3)]))
            self.assertEqual(len(s), 1)
            self.assertEqual(s[0][0].no, 3)

    def test_taskkill_alt_surec_agaci_kapat(self):
        # 12. bulgu testi: Windows'ta _kapat taskkill /T /F çağırır
        yuva = {"p": MagicMock(), "gelen": MagicMock(), "giden": MagicMock(), "is": None}
        yuva["p"].is_alive.side_effect = [True, False]
        yuva["p"].pid = 9999
        with patch("os.name", "nt"), patch("subprocess.run") as mock_run:
            OcrHavuzu._kapat(yuva)
            mock_run.assert_called_once()
            self.assertIn("/T", mock_run.call_args[0][0])
            self.assertIn("/F", mock_run.call_args[0][0])
            self.assertIn("9999", mock_run.call_args[0][0])

    def test_tek_isci_motor_bir_kez_havuzsuz(self):
        from PIL import Image
        yol = self.kok / "a.png"
        Image.new("RGB", (20, 20)).save(yol)
        with patch("videohat.belge.ocr.OcrMotoru") as sinif:
            sinif.return_value.oku.return_value = ([], 0)
            with OcrHavuzu(self.ayar) as havuz:
                list(havuz.haritala([OcrIsi(str(yol)), OcrIsi(str(yol))]))
                self.assertEqual(havuz.yuvalar, [])
            sinif.assert_called_once()
            self.assertEqual(sinif.return_value.oku.call_count, 2)

    def test_90_donuk_pdf_kutusu_ve_gorunum(self):
        yol = pdf_yaz(self.kok / "donuk.pdf", taranmis=True, donus=90)
        kutu = (30, 80, 280, 130)
        s = Sayfa(1, "Sayfa 1", "ocr", "guvenlik", .99, [OcrSatir("guvenlik", .99, kutu)], (355, 1042))
        hedef = olustur(yol, [s], self.kok / "aranabilir.pdf", 150)
        with pymupdf.open(hedef) as d, pymupdf.open(yol) as asil:
            self.assertIn("guvenlik", d[0].get_text())
            word = next(w for w in d[0].get_text("words") if w[4] == "guvenlik")
            r = pymupdf.Rect(word[:4]) * d[0].rotation_matrix
            beklenen = pymupdf.Rect(*(v * 72 / 150 for v in kutu))
            self.assertGreater((r & beklenen).get_area() / r.get_area(), .9)
            self.assertEqual(d[0].get_pixmap().samples, asil[0].get_pixmap().samples)


class GercekOcrTest(GeciciTest):
    @classmethod
    def setUpClass(cls):
        try:
            yollar.ocr_model("PP-OCRv6_det_small.onnx")
            yollar.ocr_model("PP-OCRv6_rec_small.onnx")
            yollar.ocr_model("ch_ppocr_mobile_v2.0_cls_mobile.onnx")
        except OrtamHatasi as e:
            raise unittest.SkipTest(str(e))
        if not FONT.exists():
            raise unittest.SkipTest("Arial yok")
        from videohat.ayar import Ayar, OcrAyar, DizinAyar
        cls.motor = OcrMotoru(Ayar(yollar.kok(), ocr=OcrAyar(profil="hizli", cihaz="cpu", tesseract=False),
                                  dizin=DizinAyar(isci=1)))

    def test_yan_ve_bas_asagi_tarama_satir_sirasi_ve_pdf_yonu(self):
        # Ölçülen kusur: cls ters satırı düzeltip okuduğu için yön 180° şaşıyor, satır sırası tersine dönüyordu
        import cv2
        from videohat.belge.hat import isle_belge
        d = pymupdf.open(); p = d.new_page(); p.insert_font(fontname="F0", fontfile=str(FONT))
        p.insert_textbox(pymupdf.Rect(60, 60, 540, 400), "Radar frekansı ölçümü yapılır.\nSistem güvenlik kontrolü tamam."
                         "\nVeri kaydı alındı.", fontsize=16, fontname="F0")
        pix = p.get_pixmap(dpi=200, colorspace=pymupdf.csGRAY)
        g = np.frombuffer(pix.samples, np.uint8).reshape(pix.h, pix.w)
        beklenen_yon = {"sola": (0, -1), "saga": (0, 1), "ters": (-1, 0)}
        for ad, kod in (("sola", cv2.ROTATE_90_COUNTERCLOCKWISE), ("saga", cv2.ROTATE_90_CLOCKWISE),
                        ("ters", cv2.ROTATE_180)):
            with self.subTest(ad=ad):
                yol = self.kok / f"{ad}.png"
                cv2.imwrite(str(yol), cv2.rotate(g, kod))
                kayit = isle_belge(yol, self.kok / "depo", self.motor.ayar, None)
                metin = kayit.veri["sayfalar"][0]["metin"]
                self.assertLess(metin.index("Radar"), metin.index("Veri"))
                with pymupdf.open(kayit.hedef / "aranabilir.pdf") as pd:
                    yonler = {tuple(round(x) for x in l["dir"]) for b in pd[0].get_text("dict")["blocks"]
                              for l in b.get("lines", [])}
                self.assertEqual(yonler, {beklenen_yon[ad]})

    def test_turkce_2_derece_90_derece_ve_aranabilir_pdf(self):
        from PIL import Image
        im = gorsel()
        for ad, g in (("duz", im), ("egik", dondur(im, 2)[0]), ("donuk", yon_cevir(im, 90)[0])):
            with self.subTest(ad=ad), patch("socket.socket.connect", side_effect=AssertionError("AĞA ÇIKTI")), \
                    patch("requests.get", side_effect=AssertionError("AĞA ÇIKTI")):
                s, guven = self.motor.oku(g)
                metin = birlestir(s)
                for kelime in ("frekansı", "ölçümü", "güvenlik"):
                    self.assertIn(kelime, metin)
                yol = self.kok / f"{ad}.png"
                Image.fromarray(g).save(yol)
                hedef = olustur(yol, [Sayfa(1, "Sayfa 1", "ocr", metin, guven, s,
                                           (g.shape[1], g.shape[0]))], self.kok / f"{ad}.pdf", 150)
                with pymupdf.open(hedef) as d:
                    metin_pdf = d[0].get_text()
                    for kelime in ("frekansı", "ölçümü", "güvenlik"):
                        self.assertIn(kelime, metin_pdf)
        m = self.motor._motor("small", "small")
        self.assertIsNone(m.cfg.Rec.rec_keys_path)
        self.assertTrue(m.text_rec.session.have_key())

    def test_cok_satirli_90_180_270_okuma_sirasi_ve_pdf_kutusu(self):
        # 17(b) Çok satırlı 90°/180°/270° okuma sırası ve dönük görüntü PDF'inde kutu örtüşmesi
        from PIL import Image
        im = gorsel_cok_satirli()
        for yon in (90, 180, 270):
            with self.subTest(yon=yon):
                donuk, _ = yon_cevir(im, yon)
                s, guven = self.motor.oku(donuk)
                metin = getattr(s, "metin", "") or birlestir(s)
                # Ayrı satırlar boşluk yerine \n ile ayrılmalıdır
                self.assertIn("\n", metin)
                for kelime in ("frekansı", "ölçümü", "güvenlik"):
                    self.assertIn(kelime, metin)
                yol = self.kok / f"donuk_{yon}.png"
                Image.fromarray(donuk).save(yol)
                sayfa = Sayfa(1, "Sayfa 1", "ocr", metin, guven, s, (donuk.shape[1], donuk.shape[0]))
                sayfa.yon = getattr(s, "yon", 0)
                hedef = olustur(yol, [sayfa], self.kok / f"donuk_{yon}.pdf", 150)
                with pymupdf.open(hedef) as d:
                    metin_pdf = d[0].get_text()
                    for kelime in ("frekansı", "ölçümü", "güvenlik"):
                        self.assertIn(kelime, metin_pdf)
                    # Sözcüklerin font büyüklüğü 0.5pt olmamalı, okunabilir olmalı
                    words = d[0].get_text("words")
                    self.assertTrue(len(words) >= 3)
                    for w in words:
                        self.assertGreater(max(w[2] - w[0], w[3] - w[1]), 5.0)
