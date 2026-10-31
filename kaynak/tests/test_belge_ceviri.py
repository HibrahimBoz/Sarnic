"""Belge çevirisi testleri (Görev 3)."""
import json
import logging
import tempfile
import unittest
from pathlib import Path
from unittest.mock import MagicMock, patch

from videohat.belge import ceviri_ekle, hat, onbellek
from videohat.belge.model import BelgeSonucu, Sayfa

from videohat.hatalar import CeviriHatasi


class SahteCevirmen:
    def __init__(self, hata_at=False):
        self.cagrildi = False
        self.hata_at = hata_at
        self.cagrilar = []

    def cevir(self, metinler: list[str], kaynak_dil: str) -> list[str]:
        self.cagrildi = True
        self.cagrilar.append((metinler, kaynak_dil))
        if self.hata_at:
            raise CeviriHatasi("Sahte çeviri hatası")
        return [f"tr: {m}" for m in metinler]


class TestBelgeCeviri(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.tmp_dir = Path(self.tmp.name)

    def test_ceviri_hatasi_kalici_olmaz(self):
        # İnceleme bulgusu: hata sonrası ceviri_surum ve dil yazılırsa belge bir daha denenmiyordu.
        veri = {"surum": 1, "uyarilar": [], "sayfalar": [
            {"no": 1, "metin": "Это важный отчет о безопасности системы."},
            {"no": 2, "metin": "Bu sayfa Türkçe yazılmış bir rapordur ve çevrilmez."}]}
        ceviri_ekle.ceviri_ekle(veri, SahteCevirmen(hata_at=True))
        self.assertNotIn("ceviri_surum", veri)
        self.assertNotIn("dil", veri["sayfalar"][0])  # yeniden denenecek
        self.assertEqual(veri["sayfalar"][1]["dil"], "tr")
        self.assertTrue(any("Çeviri yapılamadı" in u for u in veri["uyarilar"]))
        # Aynı hata ikinci kez değişiklik sayılmaz ve uyarı tekrar eklenmez.
        self.assertFalse(ceviri_ekle.ceviri_ekle(veri, SahteCevirmen(hata_at=True)))
        self.assertEqual(sum("Çeviri yapılamadı" in u for u in veri["uyarilar"]), 1)

        sahte = SahteCevirmen()
        ceviri_ekle.ceviri_ekle(veri, sahte)
        self.assertEqual(veri["sayfalar"][0]["ceviri"], "tr: Это важный отчет о безопасности системы.")
        self.assertEqual(veri.get("ceviri_surum"), 1)

    def test_ayni_dildeki_sayfalar_tek_cagrida_cevrilir(self):
        veri = {"surum": 1, "sayfalar": [
            {"no": i, "metin": f"Это страница номер {i} отчета о безопасности."} for i in range(1, 4)]}
        sahte = SahteCevirmen()
        ceviri_ekle.ceviri_ekle(veri, sahte)
        self.assertEqual(len(sahte.cagrilar), 1)
        self.assertEqual(len(sahte.cagrilar[0][0]), 3)

    def test_ayardaki_diller_disindaki_dil_cevrilmez(self):
        veri = {"surum": 1, "sayfalar": [{"no": 1, "metin": "Это важный отчет о безопасности системы."}]}
        sahte = SahteCevirmen()
        sahte.diller = ("en",)
        ceviri_ekle.ceviri_ekle(veri, sahte)
        self.assertFalse(sahte.cagrildi)
        self.assertEqual(veri["sayfalar"][0]["dil"], "ru")
        self.assertNotIn("ceviri", veri["sayfalar"][0])

    def test_rusca_sayfa_dil_ru_ceviri_dolu(self):
        rusca_metin = "Это важный отчет о безопасности системы и результатах тестирования радара."
        veri = {
            "surum": 1,
            "sayfalar": [
                {"no": 1, "metin": rusca_metin}
            ]
        }
        cevirmen = SahteCevirmen()
        degisti = ceviri_ekle.ceviri_ekle(veri, cevirmen)

        self.assertTrue(degisti)
        sayfa = veri["sayfalar"][0]
        self.assertEqual(sayfa.get("dil"), "ru")
        self.assertTrue(sayfa.get("ceviri"))
        self.assertTrue(sayfa["ceviri"].startswith("tr:"))
        self.assertEqual(veri.get("ceviri_surum"), 1)

    def test_turkce_sayfa_ceviri_yok(self):
        turkce_metin = "Bu radar kontrol raporu ve sistem güvenlik ölçümü belgesidir."
        veri = {
            "surum": 1,
            "sayfalar": [
                {"no": 1, "metin": turkce_metin}
            ]
        }
        cevirmen = SahteCevirmen()
        degisti = ceviri_ekle.ceviri_ekle(veri, cevirmen)

        self.assertTrue(degisti)
        sayfa = veri["sayfalar"][0]
        self.assertEqual(sayfa.get("dil"), "tr")
        self.assertNotIn("ceviri", sayfa)
        self.assertFalse(cevirmen.cagrildi)
        self.assertEqual(veri.get("ceviri_surum"), 1)

    def test_cevirmen_none_ceviri_surum_yok(self):
        rusca_metin = "Это важный отчет о безопасности системы."
        veri = {
            "surum": 1,
            "sayfalar": [
                {"no": 1, "metin": rusca_metin}
            ]
        }
        degisti = ceviri_ekle.ceviri_ekle(veri, cevirmen=None)

        # Model yokken hiçbir şey yazılmaz: dizin çeviri geçişi "dil IS NULL" ile model gelince yeniden dener.
        self.assertFalse(degisti)
        sayfa = veri["sayfalar"][0]
        self.assertNotIn("dil", sayfa)
        self.assertNotIn("ceviri", sayfa)
        self.assertNotIn("ceviri_surum", veri)

    def test_bos_sayfa_cevrilmez(self):
        veri = {
            "surum": 1,
            "sayfalar": [
                {"no": 1, "metin": "   "}
            ]
        }
        cevirmen = SahteCevirmen()
        ceviri_ekle.ceviri_ekle(veri, cevirmen)

        sayfa = veri["sayfalar"][0]
        self.assertNotIn("ceviri", sayfa)
        self.assertFalse(cevirmen.cagrildi)

    @patch("videohat.belge.hat.cikar")
    def test_onbellekteki_eski_belge_json_ocrsiz_guncellenir(self, mock_cikar):
        yol = self.tmp_dir / "belge.pdf"
        yol.write_bytes(b"%PDF-1.4 sahte pdf icerigi")
        sha = hat.sha256(yol)
        depo = self.tmp_dir / "depo"
        hedef = depo / "belgeler" / sha[:2] / sha
        hedef.mkdir(parents=True, exist_ok=True)
        belge_json = hedef / "belge.json"

        rusca_metin = "Это отчет по проверке безопасности системы."
        eski_veri = {
            "surum": 1,
            "durum": "tamam",
            "bicim": "pdf",
            "duzenlenebilir": None,
            "sayfalar": [
                {"no": 1, "metin": rusca_metin, "etiket": "1", "kaynak": "pdf", "guven": 1.0}
            ],
            "uyarilar": []
        }
        onbellek.json_yaz(belge_json, eski_veri)

        ayar = MagicMock()
        cevirmen = SahteCevirmen()

        kayit = hat.isle_belge(yol, depo, ayar, cevirmen=cevirmen)

        mock_cikar.assert_not_called()

        guncel_veri = json.loads(belge_json.read_text(encoding="utf-8"))
        self.assertEqual(guncel_veri.get("ceviri_surum"), 1)
        self.assertEqual(guncel_veri["sayfalar"][0].get("dil"), "ru")
        self.assertTrue(guncel_veri["sayfalar"][0].get("ceviri"))
        self.assertEqual(kayit.veri.get("ceviri_surum"), 1)

    def test_json_yaz_gecici_dosya_pid_tasir(self):
        import os
        yol = self.tmp_dir / "test.json"
        with patch("os.replace") as mock_replace:
            onbellek.json_yaz(yol, {"test": 1})
            mock_replace.assert_called_once()
            gecici_yol = mock_replace.call_args[0][0]
            self.assertIn(str(os.getpid()), gecici_yol.name)


if __name__ == "__main__":
    unittest.main()
