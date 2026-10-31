"""Çok dilli içerik: konuşma dili seçimi, Türkçeye özgü süzgeç, Kiril oranı ve ayar doğrulaması."""
import tempfile
import unittest
import wave
from pathlib import Path

import numpy as np

from videohat import stt
from videohat.ayar import Ayar, SttAyar
from videohat.belge.ocr import kiril_orani
from videohat.hatalar import AyarHatasi


class SahteModel:
    def __init__(self, olasiliklar):
        self.olasiliklar = olasiliklar
        self.cagri = 0

    def detect_language(self, audio, language_detection_segments=1):
        self.cagri += 1
        return self.olasiliklar[0][0], self.olasiliklar[0][1], self.olasiliklar


def _wav(yol, sn, sr=16000):
    with wave.open(str(yol), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes((np.sin(np.arange(int(sn * sr)) / 20) * 3000).astype(np.int16).tobytes())


class DilSecTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.wav = Path(self.tmp.name) / "s.wav"
        _wav(self.wav, 200)
        self.ayar = Ayar(kok=Path(self.tmp.name))

    def tearDown(self):
        self.tmp.cleanup()

    def test_izinli_disindaki_dil_kazanamaz(self):
        # Whisper "uz" der ama izinli listede yok: izinliler arasında en yüksek olan seçilir.
        model = SahteModel([("uz", 0.6), ("az", 0.3), ("tr", 0.1)])
        dil, puan = stt.dil_sec(model, self.wav, self.ayar, 16000, 200.0)
        self.assertEqual(dil, "az")
        self.assertEqual(model.cagri, stt.DIL_PENCERE_SAYISI)
        self.assertAlmostEqual(puan["az"], 0.3 * stt.DIL_PENCERE_SAYISI)

    def test_sabit_dil_modeli_cagirmaz(self):
        self.ayar.stt = SttAyar(dil="ru")
        model = SahteModel([("tr", 1.0)])
        self.assertEqual(stt.dil_sec(model, self.wav, self.ayar, 16000, 200.0)[0], "ru")
        self.assertEqual(model.cagri, 0)

    def test_kisa_kayit_tek_pencere(self):
        kisa = Path(self.tmp.name) / "k.wav"
        _wav(kisa, 10)
        model = SahteModel([("en", 0.9), ("tr", 0.1)])
        self.assertEqual(stt.dil_sec(model, kisa, self.ayar, 16000, 10.0)[0], "en")
        self.assertEqual(model.cagri, 1)

    def test_sessiz_kayit_oylanmaz(self):
        sessiz = Path(self.tmp.name) / "z.wav"
        with wave.open(str(sessiz), "wb") as w:
            w.setnchannels(1); w.setsampwidth(2); w.setframerate(16000)
            w.writeframes(np.zeros(16000 * 90, dtype=np.int16).tobytes())
        model = SahteModel([("en", 1.0)])
        self.assertEqual(stt.dil_sec(model, sessiz, self.ayar, 16000, 90.0)[0], "tr")
        self.assertEqual(model.cagri, 0)

    def test_olasilik_yoksa_ilk_izinli(self):
        model = SahteModel([("ja", 1.0)])
        self.assertEqual(stt.dil_sec(model, self.wav, self.ayar, 16000, 200.0)[0], "tr")


class IngilizceSuzgecTest(unittest.TestCase):
    METIN = "we can do it now yes all of them"

    def test_turkce_kayitta_ingilizce_halusinasyon_atilir(self):
        self.assertTrue(stt._ingilizce_halusinasyonu(self.METIN))

    def test_ingilizce_kayitta_cumle_korunur(self):
        jeton = stt._ETKIN_DIL.set("en")
        try:
            self.assertFalse(stt._ingilizce_halusinasyonu(self.METIN))
        finally:
            stt._ETKIN_DIL.reset(jeton)


class KirilOraniTest(unittest.TestCase):
    def test_oranlar(self):
        self.assertEqual(kiril_orani("Москва"), 1.0)
        self.assertEqual(kiril_orani("Azərbaycan"), 0.0)
        self.assertAlmostEqual(kiril_orani("Отчёт PDF"), 5 / 8)
        self.assertEqual(kiril_orani("   ."), 0.0)


class AyarTest(unittest.TestCase):
    def test_varsayilan_diller(self):
        a = SttAyar()
        self.assertEqual(a.dil, "otomatik")
        self.assertEqual(a.diller, ["tr", "en", "az", "ru"])

    def test_bos_diller_hata(self):
        with self.assertRaises(AyarHatasi):
            SttAyar(diller=[])

    def test_gecersiz_bicim_hata(self):
        with self.assertRaises(AyarHatasi):
            SttAyar(diller="tr")
        with self.assertRaises(AyarHatasi):
            SttAyar(dil="")

    def test_dil_kucuk_harfe(self):
        self.assertEqual(SttAyar(dil=" TR ", diller=["EN"]).diller, ["en"])
        self.assertEqual(SttAyar(dil=" TR ").dil, "tr")


if __name__ == "__main__":
    unittest.main()
