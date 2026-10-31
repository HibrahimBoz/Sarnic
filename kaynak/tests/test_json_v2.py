"""JSON v2 meta veri çıktısı için birim testleri."""
import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path

# PYTHONPATH ayarlanmamışsa kaynak/app dizinini ekle
_uygulama_dizini = Path(__file__).resolve().parent.parent / "app"
if str(_uygulama_dizini) not in sys.path:
    sys.path.insert(0, str(_uygulama_dizini))

from sarnicatolye.cihaz import CihazSecimi
from sarnicatolye.cikti import json_meta
from sarnicatolye.stt import Segment


class TestJsonV2(unittest.TestCase):
    """json_meta v2 şema ve çıktı testleri."""

    def setUp(self) -> None:
        self.gecici_dizin = tempfile.TemporaryDirectory()
        self.cikti_yolu = Path(self.gecici_dizin.name) / "test.json"

    def tearDown(self) -> None:
        self.gecici_dizin.cleanup()

    def test_eski_alanlar_korunur(self) -> None:
        """Eski üstveri alanlarının eksiksiz ve doğru değerlerle korunduğunu doğrular."""
        secim = CihazSecimi(
            device="cuda",
            compute_type="float16",
            model_ad="large-v3",
            neden="CUDA seçildi; 6000 MB boş bellek.",
        )
        sureler = {"ses_cikar": 1.25, "stt": 15.67, "cikti": 0.42}
        segmentler = [
            Segment(bas=0.0, son=3.1415, metin="İlk cümle."),
            Segment(bas=3.1415, son=7.8912, metin="İkinci cümle."),
        ]

        json_meta(segmentler, secim, sureler, self.cikti_yolu)

        with open(self.cikti_yolu, "r", encoding="utf-8") as dosya:
            veri = json.load(dosya)

        self.assertEqual(veri["cihaz"], "cuda")
        self.assertEqual(veri["compute_type"], "float16")
        self.assertEqual(veri["model"], "large-v3")
        self.assertEqual(veri["neden"], "CUDA seçildi; 6000 MB boş bellek.")
        self.assertEqual(veri["sureler"], sureler)
        self.assertEqual(veri["segment_sayisi"], 2)
        self.assertEqual(veri["sure_sn"], 7.89)

    def test_sema_surumu_ve_segment_yapisi(self) -> None:
        """sema_surumu==2 ve segment alanlarının (no, bas, son, metin) kurallara uygunluğunu doğrular."""
        secim = CihazSecimi(
            device="cpu",
            compute_type="int8",
            model_ad="medium",
            neden="CPU kullanılıyor.",
        )
        sureler = {"stt": 5.0}
        segmentler = [
            Segment(bas=0.12345, son=1.98765, metin="  Başta ve sonda boşluk var  "),
            Segment(bas=1.98765, son=3.0, metin=""),
            Segment(bas=3.0, son=4.5555, metin="   "),
            Segment(bas=4.5555, son=6.0, metin="Son metin"),
        ]

        json_meta(segmentler, secim, sureler, self.cikti_yolu)

        with open(self.cikti_yolu, "r", encoding="utf-8") as dosya:
            veri = json.load(dosya)

        self.assertEqual(veri["sema_surumu"], 2)
        self.assertEqual(len(veri["segmentler"]), veri["segment_sayisi"])
        self.assertEqual(len(veri["segmentler"]), 4)

        # Segment 0: yuvarlama ve strip kontrolü
        self.assertEqual(veri["segmentler"][0]["no"], 0)
        self.assertEqual(veri["segmentler"][0]["bas"], 0.123)
        self.assertEqual(veri["segmentler"][0]["son"], 1.988)
        self.assertEqual(veri["segmentler"][0]["metin"], "Başta ve sonda boşluk var")

        # Segment 1: boş metinli segment korunmalı
        self.assertEqual(veri["segmentler"][1]["no"], 1)
        self.assertEqual(veri["segmentler"][1]["bas"], 1.988)
        self.assertEqual(veri["segmentler"][1]["son"], 3.0)
        self.assertEqual(veri["segmentler"][1]["metin"], "")

        # Segment 2: yalnızca boşluk içeren metin boş metne dönüşmeli ve korunmalı
        self.assertEqual(veri["segmentler"][2]["no"], 2)
        self.assertEqual(veri["segmentler"][2]["bas"], 3.0)
        self.assertEqual(veri["segmentler"][2]["son"], 4.556)
        self.assertEqual(veri["segmentler"][2]["metin"], "")

        # Segment 3: son segment
        self.assertEqual(veri["segmentler"][3]["no"], 3)
        self.assertEqual(veri["segmentler"][3]["bas"], 4.556)
        self.assertEqual(veri["segmentler"][3]["son"], 6.0)
        self.assertEqual(veri["segmentler"][3]["metin"], "Son metin")

    def test_turkce_karakterler_ve_kaynak_ozet(self) -> None:
        """Türkçe karakterlerin bozulmadığını ve sha256 kaynak özetinin yeniden hesaplanınca eşleştiğini doğrular."""
        secim = CihazSecimi(
            device="cuda",
            compute_type="float16",
            model_ad="turbo",
            neden="CUDA seçildi.",
        )
        sureler = {}
        turkce_metinler = [
            "ğüşiöçİı karakterleri testi",
            "ĞÜŞİÖÇ büyük harfler",
            "Öğretmen tahtaya yazı yazdı.",
        ]
        segmentler = [
            Segment(bas=float(i), son=float(i + 1), metin=metin)
            for i, metin in enumerate(turkce_metinler)
        ]

        json_meta(segmentler, secim, sureler, self.cikti_yolu)

        # Doğrudan UTF-8 metin olarak oku ve Türkçe karakterleri kontrol et
        ham_icerik = self.cikti_yolu.read_text(encoding="utf-8")
        self.assertIn("ğüşiöçİı", ham_icerik)
        self.assertIn("ĞÜŞİÖÇ", ham_icerik)
        self.assertNotIn(r"\u", ham_icerik)  # ensure_ascii=False kontrolü

        # JSON olarak çözümle
        veri = json.loads(ham_icerik)
        self.assertEqual(veri["segment_sayisi"], len(turkce_metinler))
        self.assertEqual(len(veri["segmentler"]), len(turkce_metinler))

        for i, metin in enumerate(turkce_metinler):
            self.assertEqual(veri["segmentler"][i]["metin"], metin)

        # kaynak_ozet hesaplamasını elle tekrarla ve karşılaştır
        beklenen_json = json.dumps(veri["segmentler"], ensure_ascii=False, separators=(",", ":"))
        beklenen_ozet = hashlib.sha256(beklenen_json.encode("utf-8")).hexdigest()
        self.assertEqual(veri["kaynak_ozet"], beklenen_ozet)

    def test_bos_segment_listesi(self) -> None:
        """Boş segment listesi verildiğinde hata oluşmadığını ve doğru şema üretildiğini doğrular."""
        secim = CihazSecimi(
            device="cpu",
            compute_type="int8",
            model_ad="medium",
            neden="Boş test",
        )
        sureler = {}
        segmentler: list[Segment] = []

        json_meta(segmentler, secim, sureler, self.cikti_yolu)

        with open(self.cikti_yolu, "r", encoding="utf-8") as dosya:
            veri = json.load(dosya)

        self.assertEqual(veri["sema_surumu"], 2)
        self.assertEqual(veri["cihaz"], "cpu")
        self.assertEqual(veri["compute_type"], "int8")
        self.assertEqual(veri["model"], "medium")
        self.assertEqual(veri["neden"], "Boş test")
        self.assertEqual(veri["sureler"], {})
        self.assertEqual(veri["segment_sayisi"], 0)
        self.assertEqual(veri["sure_sn"], 0.0)
        self.assertEqual(veri["segmentler"], [])

        beklenen_bos_json = json.dumps([], ensure_ascii=False, separators=(",", ":"))
        beklenen_ozet = hashlib.sha256(beklenen_bos_json.encode("utf-8")).hexdigest()
        self.assertEqual(veri["kaynak_ozet"], beklenen_ozet)


if __name__ == "__main__":
    unittest.main()
