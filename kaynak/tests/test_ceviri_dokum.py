"""Döküm çevirisi testleri (Görev 2)."""
import json
import logging
import tempfile
import unittest
from pathlib import Path
from unittest.mock import MagicMock, patch

import numpy as np

from videohat import cikti, grup, hat
from videohat.cihaz import CihazSecimi
from videohat.stt import Segment

from videohat.hatalar import CeviriHatasi


class SahteCevirmen:
    def __init__(self, cagirilsin=True, hata_at=False, model_ad="nllb-200-distilled-1.3B"):
        self.cagrildi = False
        self.cagri_metinler = []
        self.cagri_diller = []
        self.hata_at = hata_at
        self.model_ad = model_ad

    def cevir(self, metinler: list[str], kaynak_dil: str) -> list[str]:
        self.cagrildi = True
        self.cagri_metinler.extend(metinler)
        self.cagri_diller.append(kaynak_dil)
        if self.hata_at:
            raise CeviriHatasi("Sahte çeviri hatası")
        return [f"tr: {m}" if m.strip() else "" for m in metinler]


class TestCeviriDokum(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.tmp_dir = Path(self.tmp.name)
        self.secim = CihazSecimi(
            device="cpu",
            compute_type="float32",
            model_ad="medium",
            neden="test",
        )
        self.sureler = {"stt": 1.23, "cikti": 0.05}
        self.segmentler = [
            Segment(bas=0.0, son=2.5, metin="Hello world"),
            Segment(bas=2.5, son=5.0, metin="This is a test"),
        ]

    def test_ceviriler_verilince_json_ceviri_ve_kok_alanlari(self):
        json_yolu = self.tmp_dir / "cikti_cevirili.json"
        ceviriler = ["Merhaba dünya", "Bu bir test"]
        cikti.json_meta(
            self.segmentler,
            self.secim,
            self.sureler,
            json_yolu,
            dil="en",
            ceviriler=ceviriler,
            ceviri_model="nllb-200-distilled-1.3B",
        )
        veri = json.loads(json_yolu.read_text(encoding="utf-8"))
        self.assertEqual(veri.get("ceviri_dil"), "tr")
        self.assertEqual(veri.get("ceviri_model"), "nllb-200-distilled-1.3B")
        self.assertEqual(veri.get("dil"), "en")
        self.assertEqual(len(veri["segmentler"]), 2)
        self.assertEqual(veri["segmentler"][0]["ceviri"], "Merhaba dünya")
        self.assertEqual(veri["segmentler"][1]["ceviri"], "Bu bir test")

    def test_hepsi_bos_ceviride_kok_alanlari_yazilmaz(self):
        json_yolu = self.tmp_dir / "cikti_bos_ceviri.json"
        cikti.json_meta(self.segmentler, self.secim, self.sureler, json_yolu, dil="en",
                        ceviriler=["", "  "], ceviri_model="nllb-200-distilled-1.3B")
        veri = json.loads(json_yolu.read_text(encoding="utf-8"))
        self.assertNotIn("ceviri_dil", veri)
        self.assertNotIn("ceviri_model", veri)

    def test_verilmeyince_eskisiyle_bayt_bayt_ayni(self):
        json1 = self.tmp_dir / "json1.json"
        json2 = self.tmp_dir / "json2.json"
        cikti.json_meta(self.segmentler, self.secim, self.sureler, json1, dil="tr")
        cikti.json_meta(
            self.segmentler,
            self.secim,
            self.sureler,
            json2,
            dil="tr",
            ceviriler=None,
            ceviri_model=None,
        )
        self.assertEqual(json1.read_bytes(), json2.read_bytes())
        veri = json.loads(json1.read_text(encoding="utf-8"))
        self.assertNotIn("ceviri_dil", veri)
        self.assertNotIn("ceviri_model", veri)
        for s in veri["segmentler"]:
            self.assertNotIn("ceviri", s)

    def test_kaynak_ozet_iki_durumda_esit(self):
        json_cevirisiz = self.tmp_dir / "ozet_cevirisiz.json"
        json_cevirili = self.tmp_dir / "ozet_cevirili.json"
        cikti.json_meta(self.segmentler, self.secim, self.sureler, json_cevirisiz, dil="en")
        cikti.json_meta(
            self.segmentler,
            self.secim,
            self.sureler,
            json_cevirili,
            dil="en",
            ceviriler=["Merhaba dünya", "Test"],
            ceviri_model="nllb-200-distilled-1.3B",
        )
        veri1 = json.loads(json_cevirisiz.read_text(encoding="utf-8"))
        veri2 = json.loads(json_cevirili.read_text(encoding="utf-8"))
        self.assertEqual(veri1["kaynak_ozet"], veri2["kaynak_ozet"])

    def test_bos_segment_ceviri_alani_yazilmaz(self):
        json_yolu = self.tmp_dir / "bos_segment.json"
        segs = [
            Segment(bas=0.0, son=1.0, metin="   "),
            Segment(bas=1.0, son=2.0, metin="Valid"),
        ]
        cikti.json_meta(
            segs,
            self.secim,
            self.sureler,
            json_yolu,
            ceviriler=["", "   Geçerli   "],
        )
        veri = json.loads(json_yolu.read_text(encoding="utf-8"))
        self.assertNotIn("ceviri", veri["segmentler"][0])
        self.assertEqual(veri["segmentler"][1]["ceviri"], "Geçerli")

    @patch("videohat.hat._ses_akisi_var", return_value=True)
    @patch("videohat.hat._video_akisi_var", return_value=False)
    @patch("videohat.ses.cikar")
    @patch("videohat.ses.temizle")
    @patch("videohat.ses.stt_icin")
    @patch("videohat.cihaz.sec")
    @patch("videohat.stt.yaz")
    def test_dil_tr_iken_ceviriciyi_cagirmaz(
        self, mock_stt_yaz, mock_cihaz_sec, mock_stt_icin, mock_temizle, mock_cikar, mock_video_var, mock_ses_var
    ):
        mock_cihaz_sec.return_value = self.secim
        mock_stt_yaz.return_value = [Segment(0.0, 1.0, "Merhaba dünya")]
        cevirmen = SahteCevirmen()

        from videohat import stt as stt_mod
        stt_mod.SON_DIL = "tr"

        ayar = MagicMock()
        ayar.kok = self.tmp_dir
        ayar.stt.terimler_dosyasi = "terimler.txt"
        ayar.genel.ara_dosya_sakla = False
        (self.tmp_dir / "terimler.txt").write_text("", encoding="utf-8")

        video_dosya = self.tmp_dir / "test.mp4"
        video_dosya.write_bytes(b"sahte video")

        bayraklar = hat.Bayraklar()
        sonuc = hat.isle_tek(video_dosya, self.tmp_dir / "cikti", ayar, bayraklar, cevirmen=cevirmen)

        self.assertTrue(sonuc.basarili)
        self.assertFalse(cevirmen.cagrildi)
        json_dosya = self.tmp_dir / "cikti" / "test.json"
        self.assertTrue(json_dosya.is_file())
        veri = json.loads(json_dosya.read_text(encoding="utf-8"))
        self.assertNotIn("ceviri_dil", veri)

    @patch("videohat.hat._ses_akisi_var", return_value=True)
    @patch("videohat.hat._video_akisi_var", return_value=False)
    @patch("videohat.ses.cikar")
    @patch("videohat.ses.temizle")
    @patch("videohat.ses.stt_icin")
    @patch("videohat.cihaz.sec")
    @patch("videohat.stt.yaz")
    def test_cevirici_ceviri_hatasi_atinca_isleme_basarili_gunlukte_uyari(
        self, mock_stt_yaz, mock_cihaz_sec, mock_stt_icin, mock_temizle, mock_cikar, mock_video_var, mock_ses_var
    ):
        mock_cihaz_sec.return_value = self.secim
        mock_stt_yaz.return_value = [Segment(0.0, 1.0, "Hello world")]
        cevirmen = SahteCevirmen(hata_at=True)

        from videohat import stt as stt_mod
        stt_mod.SON_DIL = "en"

        ayar = MagicMock()
        ayar.kok = self.tmp_dir
        ayar.stt.terimler_dosyasi = "terimler.txt"
        ayar.genel.ara_dosya_sakla = False
        (self.tmp_dir / "terimler.txt").write_text("", encoding="utf-8")

        video_dosya = self.tmp_dir / "test_hata.mp4"
        video_dosya.write_bytes(b"sahte video")

        bayraklar = hat.Bayraklar()
        with self.assertLogs("videohat", level="WARNING") as cm:
            sonuc = hat.isle_tek(video_dosya, self.tmp_dir / "cikti_hata", ayar, bayraklar, cevirmen=cevirmen)

        self.assertTrue(sonuc.basarili)
        self.assertIsNone(sonuc.hata)
        self.assertTrue(any("Çeviri yapılamadı" in m for m in cm.output))
        json_dosya = self.tmp_dir / "cikti_hata" / "test_hata.json"
        veri = json.loads(json_dosya.read_text(encoding="utf-8"))
        self.assertNotIn("ceviri_dil", veri)

    def test_cok_acili_ders_jsonunda_ceviri_ve_dil_korunur(self):
        klasor = self.tmp_dir / "ders1"
        (klasor / "cam1").mkdir(parents=True)
        (klasor / "cam1" / "01.mp4").touch()

        seg = Segment(0.0, 3.0, "Welcome everyone", ceviri="Herkese hoş geldiniz")
        sonuc_transkript = grup.TranskriptSonucu([seg], meta={
            "dil": "en", "ceviri_dil": "tr", "ceviri_model": "nllb-200-distilled-1.3B-ct2-int8",
        })

        grup_sonuc = grup.isle_grup(
            klasor,
            prob_fn=lambda d: (0.0, 10.0),
            ses_yukle_fn=lambda d, sr=8000: np.ones(10 * sr, dtype=np.float32),
            transkript_fn=lambda ses: sonuc_transkript,
        )
        self.assertTrue(grup_sonuc.basarili, grup_sonuc.hata)
        veri = json.loads((grup_sonuc.cikis_klasoru / "ders1.json").read_text(encoding="utf-8"))
        self.assertEqual(veri.get("dil"), "en")
        self.assertEqual(veri.get("ceviri_dil"), "tr")
        self.assertEqual(veri.get("ceviri_model"), "nllb-200-distilled-1.3B-ct2-int8")
        self.assertEqual(veri["segmentler"][0]["ceviri"], "Herkese hoş geldiniz")

if __name__ == "__main__":
    unittest.main()
