import json
import unittest
from dataclasses import asdict

from videohat import yollar
from videohat.ayar import DizinAyar, OcrAyar
from videohat.hatalar import AyarHatasi
from videohat.dizin.normal import icerik_normalle, dosya_normalle


class NormalTest(unittest.TestCase):
    def test_icerik_ortak_vakalar(self):
        for v in json.loads(yollar.ortak("normalizasyon_vakalari.json").read_text(encoding="utf-8")):
            with self.subTest(girdi=v["girdi"]):
                self.assertEqual(icerik_normalle(v["girdi"]), v["beklenen"])

    def test_dosya_ortak_vakalar(self):
        for v in json.loads(yollar.ortak("dosya_normalizasyon_vakalari.json").read_text(encoding="utf-8")):
            with self.subTest(girdi=v["girdi"]):
                self.assertEqual(dosya_normalle(v["girdi"]), v["beklenen"])

    def test_dosya_ayrac_noktalama_bosluk_ve_bmp(self):
        self.assertEqual(dosya_normalle("D:\\Arşiv\\Yönerge (son).docx"), "d:\\arsiv\\yonerge (son).docx")
        self.assertEqual(dosya_normalle("A  B😀.PDF"), "a  b😀.pdf")
        self.assertEqual(icerik_normalle("a\x00b\u0301😀c"), "a b c")

    def test_ayar_varsayilanlar_ve_gecersizler(self):
        self.assertEqual((DizinAyar().yayin_dk, DizinAyar().tarama_dk, DizinAyar().max_dosya_mb), (30, 15, 2048))
        self.assertEqual((OcrAyar().dpi, OcrAyar().min_metin, OcrAyar().ikinci_esik), (300, 40, .8))
        for sinif, alan, deger in ((DizinAyar, "isci", -1), (DizinAyar, "kokler", "x"),
                                  (DizinAyar, "sakla_yayin", 0), (DizinAyar, "max_dosya_mb", 1.5),
                                  (OcrAyar, "profil", "yanlış"), (OcrAyar, "dpi", True),
                                  (OcrAyar, "ikinci_esik", 1.1), (OcrAyar, "cihaz", "gpu"),
                                  (OcrAyar, "sayfa_zaman_asimi_sn", float("nan"))):
            with self.subTest(alan=alan), self.assertRaises(AyarHatasi):
                sinif(**{alan: deger})
