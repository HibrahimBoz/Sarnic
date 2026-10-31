"""VideoHat upscale yardımcı fonksiyonları birim testleri."""
import sys
import unittest
from pathlib import Path

kok = Path(__file__).resolve().parent.parent / "app"
if str(kok) not in sys.path:
    sys.path.insert(0, str(kok))

from videohat.goruntu import (
    _fps_oran,
    _olcek_sec,
    _parca_plani,
    _re_komutu,
)
from videohat.hatalar import UpscaleHatasi


class TestFpsOran(unittest.TestCase):
    """_fps_oran fonksiyonunun dönüştürme ve hata yönetimi testleri."""

    def test_kesirli_fps(self) -> None:
        """30000/1001 ve 24000/1001 gibi kesirli oranlar doğru yuvarlanmalı."""
        self.assertAlmostEqual(_fps_oran("30000/1001"), 30000 / 1001)
        self.assertAlmostEqual(_fps_oran("24000/1001"), 24000 / 1001)

    def test_tam_sayi_fps(self) -> None:
        """Tam sayı ve ondalıklı metinler doğru çevrilmeli."""
        self.assertEqual(_fps_oran("25"), 25.0)
        self.assertEqual(_fps_oran("30"), 30.0)
        self.assertEqual(_fps_oran("60.0"), 60.0)

    def test_bozuk_ve_gecersiz_fps(self) -> None:
        """Bozuk, boş veya 0 olan girdilerde varsayılan 30.0 dönmeli."""
        self.assertEqual(_fps_oran("0"), 30.0)
        self.assertEqual(_fps_oran("0/0"), 30.0)
        self.assertEqual(_fps_oran("30/0"), 30.0)
        self.assertEqual(_fps_oran("-25"), 30.0)
        self.assertEqual(_fps_oran(""), 30.0)
        self.assertEqual(_fps_oran("bozuk_fps"), 30.0)
        self.assertEqual(_fps_oran(None), 30.0)  # type: ignore[arg-type]


class TestOlcekSec(unittest.TestCase):
    """_olcek_sec fonksiyonunun çözünürlük ve model seçimi testleri."""

    def test_kaynak_buyuk_esit_hedef(self) -> None:
        """Kaynak yükseklik hedefe eşit veya büyükse ölçek 0 dönmeli."""
        self.assertEqual(_olcek_sec(1080, 1080, "hizli"), 0)
        self.assertEqual(_olcek_sec(1440, 1080, "hizli"), 0)
        self.assertEqual(_olcek_sec(1080, 1080, "agir"), 0)
        self.assertEqual(_olcek_sec(2160, 1080, "genel"), 0)

    def test_hizli_model_olcekler(self) -> None:
        """Hızlı model: kaynak*2 >= hedef ise 2, değilse 4 dönmeli."""
        # 720 * 2 = 1440 >= 1080 -> 2
        self.assertEqual(_olcek_sec(720, 1080, "hizli"), 2)
        # 540 * 2 = 1080 >= 1080 -> 2
        self.assertEqual(_olcek_sec(540, 1080, "hizli"), 2)
        # 480 * 2 = 960 < 1080 -> 4
        self.assertEqual(_olcek_sec(480, 1080, "hizli"), 4)
        # 360 * 2 = 720 < 1080 -> 4
        self.assertEqual(_olcek_sec(360, 1080, "hizli"), 4)

    def test_agir_ve_genel_modeller(self) -> None:
        """Ağır ve genel modellerde yalnızca x4 olduğu için her zaman 4 dönmeli."""
        self.assertEqual(_olcek_sec(720, 1080, "agir"), 4)
        self.assertEqual(_olcek_sec(480, 1080, "agir"), 4)
        self.assertEqual(_olcek_sec(720, 1080, "genel"), 4)
        self.assertEqual(_olcek_sec(360, 1080, "genel"), 4)

    def test_bilinmeyen_model_hata(self) -> None:
        """Bilinmeyen model adında UpscaleHatasi fırlatılmalı."""
        with self.assertRaises(UpscaleHatasi):
            _olcek_sec(720, 1080, "bilinmeyen_model")
        with self.assertRaises(UpscaleHatasi):
            _olcek_sec(1080, 1080, "gecersiz")


class TestParcaPlani(unittest.TestCase):
    """_parca_plani dilimleme mantığı ve sınır değer testleri."""

    def test_sifir_ve_negatif_kare(self) -> None:
        """toplam_kare <= 0 olduğunda boş liste dönmeli."""
        self.assertEqual(_parca_plani(0, 25.0, 120), [])
        self.assertEqual(_parca_plani(-10, 25.0, 120), [])

    def test_bir_karelik_video(self) -> None:
        """1 karelik video tek parça olarak doğru planlanmalı."""
        plan = _parca_plani(1, 25.0, 120)
        self.assertEqual(plan, [(0, 1, 0.0)])

    def test_tam_bolunen_parcalar(self) -> None:
        """Kare sayısı parça boyutuna tam bölündüğünde tüm parçalar eşit olmalı."""
        # 25 fps, 4 saniye -> parça başına 100 kare
        toplam_kare = 300
        plan = _parca_plani(toplam_kare, 25.0, 4)
        beklenen = [
            (0, 100, 0.0),
            (1, 100, 4.0),
            (2, 100, 8.0),
        ]
        self.assertEqual(plan, beklenen)
        self.assertEqual(sum(k for _, k, _ in plan), toplam_kare)

    def test_tam_bolunmeyen_parcalar(self) -> None:
        """Son parça kalan kare sayısını içermeli."""
        # 25 fps, 4 saniye -> parça başına 100 kare, toplam 250 kare
        toplam_kare = 250
        plan = _parca_plani(toplam_kare, 25.0, 4)
        beklenen = [
            (0, 100, 0.0),
            (1, 100, 4.0),
            (2, 50, 8.0),
        ]
        self.assertEqual(plan, beklenen)
        self.assertEqual(sum(k for _, k, _ in plan), toplam_kare)

    def test_baslangic_suresi_hesabi(self) -> None:
        """baslangic_sn değerleri ilk_kare_no / fps formülüne tam uymalı."""
        fps = 29.97
        parca_sn = 10
        toplam_kare = 1000
        plan = _parca_plani(toplam_kare, fps, parca_sn)
        parca_kare = max(1, round(parca_sn * fps))

        for idx, k_sayisi, bas_sn in plan:
            ilk_kare_no = idx * parca_kare
            self.assertAlmostEqual(bas_sn, ilk_kare_no / fps, places=6)


class TestReKomutu(unittest.TestCase):
    """_re_komutu komut satırı parametreleri ve model eşlemeleri testleri."""

    def test_hizli_model(self) -> None:
        """Hızlı model realesr-animevideov3 ile eşleşmeli."""
        re_exe = Path("C:/araclar/realesrgan-ncnn-vulkan.exe")
        giris = Path("C:/ara/giris")
        cikis = Path("C:/ara/cikis")
        komut = _re_komutu(re_exe, giris, cikis, "hizli", 2, None, "jpg")
        beklenen = [
            str(re_exe),
            "-i", str(giris),
            "-o", str(cikis),
            "-n", "realesr-animevideov3",
            "-s", "2",
            "-f", "jpg",
        ]
        self.assertEqual(komut, beklenen)

    def test_agir_model(self) -> None:
        """Ağır model realesrgan-x4plus ile eşleşmeli."""
        re_exe = Path("C:/araclar/realesrgan-ncnn-vulkan.exe")
        giris = Path("C:/ara/giris")
        cikis = Path("C:/ara/cikis")
        komut = _re_komutu(re_exe, giris, cikis, "agir", 4, None, "png")
        beklenen = [
            str(re_exe),
            "-i", str(giris),
            "-o", str(cikis),
            "-n", "realesrgan-x4plus",
            "-s", "4",
            "-f", "png",
        ]
        self.assertEqual(komut, beklenen)

    def test_genel_model_model_dizini_ile(self) -> None:
        """Genel model -m bayrağı ile özel model dizinini almalı."""
        re_exe = Path("C:/araclar/realesrgan-ncnn-vulkan.exe")
        giris = Path("C:/ara/giris")
        cikis = Path("C:/ara/cikis")
        model_dizini = Path("C:/araclar/realesrgan/genel/models")
        komut = _re_komutu(re_exe, giris, cikis, "genel", 4, model_dizini, "jpg")
        beklenen = [
            str(re_exe),
            "-i", str(giris),
            "-o", str(cikis),
            "-n", "realesr-animevideov3",
            "-s", "4",
            "-f", "jpg",
            "-m", str(model_dizini),
        ]
        self.assertEqual(komut, beklenen)

    def test_genel_model_dizini_none_hata(self) -> None:
        """Genel model için model_dizini None ise UpscaleHatasi verilmeli."""
        re_exe = Path("C:/araclar/realesrgan-ncnn-vulkan.exe")
        giris = Path("C:/ara/giris")
        cikis = Path("C:/ara/cikis")
        with self.assertRaises(UpscaleHatasi):
            _re_komutu(re_exe, giris, cikis, "genel", 4, None, "jpg")

    def test_bilinmeyen_model_hata(self) -> None:
        """Bilinmeyen model adında UpscaleHatasi verilmeli."""
        re_exe = Path("C:/araclar/realesrgan-ncnn-vulkan.exe")
        giris = Path("C:/ara/giris")
        cikis = Path("C:/ara/cikis")
        with self.assertRaises(UpscaleHatasi):
            _re_komutu(re_exe, giris, cikis, "bilinmeyen", 4, None, "jpg")


if __name__ == "__main__":
    unittest.main()
