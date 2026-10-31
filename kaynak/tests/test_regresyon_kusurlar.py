"""Video Transkriptör - 8 Kusur Regresyon Testleri.

Hafif, GPU/model/ffmpeg gerektirmeyen birim testleri.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from unittest.mock import patch
import wave

import numpy as np

# Modül arama yolunu ekle
kok_dizin = Path(__file__).resolve().parent.parent
app_dizin = kok_dizin / "app"
for p in (str(kok_dizin), str(app_dizin)):
    if p not in sys.path:
        sys.path.insert(0, p)

from videohat import __main__ as main_mod, cikti, ders_json, grup, izle, parca_zinciri, ses
from videohat.hat import Bayraklar, Sonuc
from videohat.parca_zinciri import Parca, SanalZamanCizelgesi
from videohat.stt import Segment


class TestKusur1KanalBulma(unittest.TestCase):
    """1. Kusur: grup.py KALIP_REGEX ve kanallari_bul ses uzantıları ve sayısız dosyalar."""

    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def test_duz_klasor_sayisiz_dosya_tek_kanal(self):
        """Düz klasörde ders.mp4 boş dönmemeli, 'ders' stem'i ile tek kanal olmalıdır."""
        (self.tmp / "ders.mp4").touch()
        kanallar = grup.kanallari_bul(self.tmp)
        self.assertIn("ders", kanallar)
        self.assertEqual(len(kanallar["ders"]), 1)
        self.assertEqual(kanallar["ders"][0].name, "ders.mp4")

    def test_ses_uzantilari_sayili_kalip(self):
        """cam1.mp3 ve cam2.m4a ses uzantılarıyla bulunmalı ve 'cam' kanalında toplanmalıdır."""
        (self.tmp / "cam1.mp3").touch()
        (self.tmp / "cam2.m4a").touch()
        kanallar = grup.kanallari_bul(self.tmp)
        self.assertIn("cam", kanallar)
        self.assertEqual(len(kanallar["cam"]), 2)

    def test_kanal_bulma_yeni_ve_eski_cikti_klasorlerini_atlar(self):
        """_islenmis ve eski _videoperisi alt klasörleri kamera kanalı sayılmaz."""
        (self.tmp / "cam1").mkdir()
        (self.tmp / "cam1" / "001.mp4").touch()
        for ad in ("ders_islenmis", "ders_videoperisi"):
            cikti = self.tmp / ad
            cikti.mkdir()
            (cikti / "ders_islenmis.mp4").touch()

        kanallar = grup.kanallari_bul(self.tmp)

        self.assertEqual(list(kanallar), ["cam1"])

    def test_kalip_regex_video_ve_ses_uzantilarini_icerir(self):
        """KALIP_REGEX, VIDEO_UZANTILARI içindeki tüm uzantıları tanımalıdır."""
        for uzanti in grup.VIDEO_UZANTILARI:
            isim = f"kamera1_01{uzanti}"
            m = grup.KALIP_REGEX.match(isim)
            self.assertIsNotNone(m, f"Regex {uzanti} uzantısını eşleştiremedi")


class TestKusur2ReferansSecRMS(unittest.TestCase):
    """2. Kusur: varsayılan enerji (RMS) fonksiyonu ve sessiz mikrofonun referans seçilmemesi."""

    def test_varsayilan_ses_enerjisi_rms(self):
        """varsayilan_ses_enerjisi sessiz sinyalde 0, sesli sinyalde pozitif RMS döndürür."""
        sessiz = np.zeros(16000, dtype=np.float32)
        sesli = np.ones(16000, dtype=np.float32) * 0.5

        self.assertAlmostEqual(grup.varsayilan_ses_enerjisi(sessiz), 0.0)
        self.assertAlmostEqual(grup.varsayilan_ses_enerjisi(sesli), 0.5, places=4)

    def test_referans_sec_sessiz_mikrofonu_eleme(self):
        """ses_enerji_fn verilmediğinde alfabetik ilk olan sessiz kanal yerine enerjili kanal seçilir."""
        sessiz = np.zeros(8000, dtype=np.float32)
        sesli = np.sin(np.linspace(0, 100, 8000)).astype(np.float32) * 0.4

        # a_sessiz alfabetik olarak b_sesli'den önde
        kanallar = {"a_sessiz": sessiz, "b_sesli": sesli}
        secilen = grup.referans_sec(kanallar)
        self.assertEqual(secilen, "b_sesli")


class TestKusur3TamBantTranskriptKomut(unittest.TestCase):
    """3. Kusur: transkript için tam bant (16k) komut oluşturucular ve wave örnek hızı."""

    def test_ses_cikar_komutu_ar_parametresi(self):
        """ses.cikar_komutu verilen sr değerini -ar parametresine koymalıdır."""
        with patch("videohat.yollar.ffmpeg", return_value=Path("ffmpeg.exe")):
            cmd = ses.cikar_komutu("giris.mp4", "cikis.wav", sr=16000)
        self.assertIn("-ar", cmd)
        idx = cmd.index("-ar")
        self.assertEqual(cmd[idx + 1], "16000")

    def test_ses_yukle_komutu_ar_parametresi(self):
        """grup.ses_yukle_komutu verilen sr değerini -ar parametresine koymalıdır."""
        with patch("videohat.yollar.ffmpeg", return_value=Path("ffmpeg.exe")):
            cmd = grup.ses_yukle_komutu("giris.mp4", sr=16000)
        self.assertIn("-ar", cmd)
        idx = cmd.index("-ar")
        self.assertEqual(cmd[idx + 1], "16000")

    def test_varsayilan_transkript_wave_ornek_hizi(self):
        """_varsayilan_transkript geçici wav dosyasını 16000 Hz ile yazmalıdır."""
        from unittest.mock import patch

        yakalanan_framerate = []

        def sahte_isle_tek(video, hedef_cikis, ayar, bayraklar):
            with wave.open(str(video), "rb") as wf:
                yakalanan_framerate.append(wf.getframerate())
            meta = {
                "sema_surumu": 2,
                "segmentler": [{"bas": 0.0, "son": 1.0, "metin": "Test"}],
            }
            (Path(hedef_cikis) / f"{video.stem}.json").write_text(json.dumps(meta), encoding="utf-8")
            return Sonuc(ad=video.stem, basarili=True, hata=None)

        tmp_cikis = Path(tempfile.mkdtemp())
        try:
            ref_ses = np.zeros(16000, dtype=np.float32)
            with patch("videohat.hat.isle_tek", side_effect=sahte_isle_tek):
                _ = grup._varsayilan_transkript(ref_ses, tmp_cikis, None, None, sr=16000)
            self.assertEqual(yakalanan_framerate, [16000])
        finally:
            shutil.rmtree(tmp_cikis, ignore_errors=True)


class TestKusur4CikisKlasoruCakisma(unittest.TestCase):
    """4. Kusur: --cikis verildiğinde bağıl alt klasör ve stem çakışma koruması."""

    def test_tek_dosya_girdisinde_cikis_aynen_korunur(self):
        """Tek dosya girdisinde --cikis doğrudan kullanılır."""
        video = Path("/girdi/ders.mp4")
        girdi_koku = Path("/girdi/ders.mp4")
        cikis = main_mod._cikis_belirle(video, girdi_koku, "/hedef/cikti", tek_dosya_mi=True)
        self.assertEqual(cikis, Path("/hedef/cikti"))

    def test_klasor_girdisinde_bagil_alt_klasor_ve_islenmis_eki(self):
        """Klasör girdisinde girdi köküne göre bağıl klasör yapısı korunur."""
        girdi_koku = Path("/girdi/dersler")
        video = Path("/girdi/dersler/bolum1/ders.mp4")
        cikis = main_mod._cikis_belirle(video, girdi_koku, "/hedef/cikti", tek_dosya_mi=False)
        self.assertEqual(cikis, Path("/hedef/cikti/bolum1/ders_islenmis"))

    def test_topla_eski_ve_yeni_cikti_klasorlerini_girdiden_atlar(self):
        kok = Path(tempfile.mkdtemp())
        try:
            orijinal = kok / "orijinal.mp4"
            orijinal.touch()
            yeni = kok / "cikti_islenmis"
            eski = kok / "cikti_videoperisi"
            yeni.mkdir()
            eski.mkdir()
            (yeni / "ic_video.mp4").touch()
            (eski / "eski_video.mp4").touch()

            self.assertEqual(main_mod._topla(kok), [orijinal])
            self.assertEqual(main_mod._topla(eski), [])
        finally:
            shutil.rmtree(kok, ignore_errors=True)

    def test_ayni_stem_cakismasi_benzersiz_ad(self):
        """Aynı klasörde a.mp4 ve a.mkv gibi çakışan dosyalara benzersiz çıktı klasörü verilir."""
        girdi_koku = Path("/girdi/dersler")
        v1 = Path("/girdi/dersler/a.mp4")
        v2 = Path("/girdi/dersler/a.mkv")
        kullanilan = set()

        c1 = main_mod._cikis_belirle(v1, girdi_koku, "/hedef/cikti", tek_dosya_mi=False, kullanilan=kullanilan)
        c2 = main_mod._cikis_belirle(v2, girdi_koku, "/hedef/cikti", tek_dosya_mi=False, kullanilan=kullanilan)

        self.assertNotEqual(c1, c2)
        self.assertEqual(c1, Path("/hedef/cikti/a_islenmis"))
        self.assertEqual(c2, Path("/hedef/cikti/a_mkv_islenmis"))

    def test_ayri_calistirmalarda_mevcut_dolu_klasor_cakisma_korumasi(self):
        """Ayrı çalıştırmalarda (kullanilan sıfırlandığında) mevcut dolu klasör ezilmez, benzersiz ad seçilir."""
        tmp = Path(tempfile.mkdtemp())
        try:
            girdi_koku = Path("/girdi/dersler")
            v1 = Path("/girdi/dersler/a.mp4")
            v2 = Path("/girdi/dersler/a.mkv")

            c1 = main_mod._cikis_belirle(v1, girdi_koku, tmp, tek_dosya_mi=False, kullanilan=set())
            self.assertEqual(c1, tmp / "a_islenmis")
            c1.mkdir(parents=True, exist_ok=True)
            (c1 / "a.json").write_text("{}", encoding="utf-8")

            # İkinci dosya ayrı bir çalıştırmada (sıfırlanmış kullanilan kümesi) çağrıldığında
            c2 = main_mod._cikis_belirle(v2, girdi_koku, tmp, tek_dosya_mi=False, kullanilan=set())
            self.assertNotEqual(c1, c2)
            self.assertEqual(c2, tmp / "a_mkv_islenmis")

            # a_mkv_islenmis de doluysa bir sonraki çalıştırma sayacı artırır
            c2.mkdir(parents=True, exist_ok=True)
            (c2 / "a.json").write_text("{}", encoding="utf-8")
            v3 = Path("/girdi/dersler/a.avi")
            c3 = main_mod._cikis_belirle(v3, girdi_koku, tmp, tek_dosya_mi=False, kullanilan=set())
            self.assertEqual(c3, tmp / "a_avi_islenmis")
        finally:
            shutil.rmtree(tmp, ignore_errors=True)


class TestKusur5GrupUlasimVeBat(unittest.TestCase):
    """5. Kusur: isle-grup komutu, grup.bat, baslat.ps1 ve KULLANIM.txt entegrasyonu."""

    def test_isle_grup_cli_model_ve_cihaz_secenekleri(self):
        """videohat isle-grup alt komutu --model ve --cihaz seçeneklerini kabul eder."""
        ap = main_mod.parser_olustur()
        args = ap.parse_args(["isle-grup", "D:/ders", "--model", "large-v3", "--cihaz", "cuda"])
        self.assertEqual(args.model, "large-v3")
        self.assertEqual(args.cihaz, "cuda")

    def test_baslat_ps1_utf8_bom(self):
        """baslat.ps1 dosyasının başında Windows PowerShell 5.1 uyumu için UTF-8 BOM (EF BB BF) bulunmalıdır."""
        ps1_yolu = kok_dizin / "baslat" / "baslat.ps1"
        self.assertTrue(ps1_yolu.exists())
        self.assertTrue(ps1_yolu.read_bytes().startswith(b"\xef\xbb\xbf"))

    def test_grup_bat_dosyasi_mevcut_ve_isle_grup_cagirir(self):
        """kaynak/bat/grup.bat dosyası mevcut olmalı ve 'videohat isle-grup %*' çalıştırmalıdır."""
        bat_yolu = kok_dizin / "bat" / "grup.bat"
        self.assertTrue(bat_yolu.exists(), f"grup.bat bulunamadı: {bat_yolu}")
        icerik = bat_yolu.read_text(encoding="utf-8")
        self.assertIn("videohat isle-grup %*", icerik)

    def test_baslat_ps1_grup_secenegi(self):
        """baslat.ps1 menüsünde grup ders seçeneği bulunmalıdır."""
        ps1_yolu = kok_dizin / "baslat" / "baslat.ps1"
        self.assertTrue(ps1_yolu.exists())
        icerik = ps1_yolu.read_text(encoding="utf-8")
        self.assertIn("grup.bat", icerik)
        self.assertIn("Çok kameralı", icerik)

    def test_kullanim_txt_grup_bolumu(self):
        """KULLANIM.txt dosyasında çok kameralı / grup işleme bölümü bulunmalıdır."""
        txt_yolu = kok_dizin / "KULLANIM.txt"
        self.assertTrue(txt_yolu.exists())
        icerik = txt_yolu.read_text(encoding="utf-8")
        self.assertIn("grup.bat", icerik)


class TestKusur6MetaAlanlariAktarimi(unittest.TestCase):
    """6. Kusur: hat.isle_tek geçici çıktısındaki üstveri alanlarının grup JSON çıktısına aktarımı."""

    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def test_isle_tek_meta_alanlari_grup_jsona_aktarilir(self):
        """cihaz, compute_type, model ve süreler alanları sabit değerler yerine isle_tek'ten alınır."""
        from unittest.mock import patch

        (self.tmp / "cam1").mkdir()
        (self.tmp / "cam1" / "01.mp4").touch()

        def sahte_prob(d):
            return 0.0, 5.0

        def sahte_ses(d, sr=8000):
            return np.zeros(5 * sr, dtype=np.float32)

        def sahte_isle_tek(video, hedef_cikis, ayar, bayraklar):
            hedef_path = Path(hedef_cikis)
            meta = {
                "sema_surumu": 2,
                "cihaz": "cuda",
                "compute_type": "float16",
                "model": "large-v3",
                "neden": "gpu_bellek_yeterli",
                "sureler": {"stt": 7.8, "ses_cikar": 0.4},
                "segmentler": [{"bas": 0.0, "son": 2.0, "metin": "Örnek metin"}],
            }
            (hedef_path / f"{video.stem}.json").write_text(json.dumps(meta), encoding="utf-8")
            return Sonuc(ad=video.stem, basarili=True, hata=None)

        bay = Bayraklar(model="large-v3", cihaz="cuda")
        with patch("videohat.hat.isle_tek", side_effect=sahte_isle_tek):
            sonuc = grup.isle_grup(
                self.tmp,
                bayraklar=bay,
                prob_fn=sahte_prob,
                ses_yukle_fn=sahte_ses,
            )

        self.assertTrue(sonuc.basarili)
        json_icerik = json.loads((sonuc.cikis_klasoru / f"{self.tmp.name}.json").read_text(encoding="utf-8"))
        self.assertEqual(json_icerik["cihaz"], "cuda")
        self.assertEqual(json_icerik["compute_type"], "float16")
        self.assertEqual(json_icerik["model"], "large-v3")
        self.assertEqual(json_icerik["sureler"]["stt"], 7.8)


class TestKusur7AyniBaslangicZamaniZincirleme(unittest.TestCase):
    """7. Kusur: creation_time aynı olan parçaların çakışma sayılmayıp zincirlenmesi."""

    def test_ayni_baslangic_zamani_parcalari_zincirler(self):
        """Parca('a', 1000, 600) ve Parca('b', 1000, 600) iki dilim olarak korunmalıdır."""
        parcalar = [
            Parca("a.mp4", 1000.0, 600.0),
            Parca("b.mp4", 1000.0, 600.0),
        ]
        cizelge = SanalZamanCizelgesi(parcalar)

        self.assertEqual(len(cizelge), 2)
        self.assertAlmostEqual(cizelge.toplam_sure, 1200.0)
        self.assertEqual(cizelge[0].dosya, "a.mp4")
        self.assertEqual(cizelge[1].dosya, "b.mp4")
        self.assertAlmostEqual(cizelge[0].sanal_bas, 0.0)
        self.assertAlmostEqual(cizelge[0].sanal_son, 600.0)
        self.assertAlmostEqual(cizelge[1].sanal_bas, 600.0)
        self.assertAlmostEqual(cizelge[1].sanal_son, 1200.0)
        self.assertTrue(any("eşit" in u for u in cizelge.uyarilar))


class TestKusur8GeriAlYetimKlasor(unittest.TestCase):
    """8. Kusur: _geri_al açılışta taşırken eşleşen yetim bitti\\<ad> klasörünü temizler."""

    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        self.isleniyor = self.tmp / "isleniyor"
        self.gelen = self.tmp / "gelen"
        self.bitti = self.tmp / "bitti"
        self.isleniyor.mkdir()
        self.gelen.mkdir()
        self.bitti.mkdir()

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def test_geri_al_yetim_bitti_klasorunu_temizler(self):
        """Yarım kalan ders.mp4 geri taşındığında bitti\\ders klasörü silinmelidir."""
        yarim_video = self.isleniyor / "ders.mp4"
        yarim_video.write_text("sahte video")

        yetim_bitti = self.bitti / "ders"
        yetim_bitti.mkdir()
        (yetim_bitti / "yarim_dosya.tmp").write_text("artık veri")

        izle._geri_al(self.isleniyor, self.gelen, self.bitti)

        # Video gelen klasörüne taşınmış olmalı
        self.assertTrue((self.gelen / "ders.mp4").exists())
        self.assertFalse(yarim_video.exists())

        # Yetim bitti/ders klasörü silinmiş olmalı
        self.assertFalse(yetim_bitti.exists())

    def test_geri_al_basarili_bitmis_klasoru_korur(self):
        """ders.mp4 başarıyla bitmişken (json/srt var), yarım kalan ders.mov geri alındığında bitti\\ders silinmemelidir."""
        bitmis_bitti = self.bitti / "ders"
        bitmis_bitti.mkdir()
        (bitmis_bitti / "ders.srt").write_text("1\n00:00:00,000 --> 00:00:01,000\nTest", encoding="utf-8")
        (bitmis_bitti / "ders.json").write_text('{"segmentler": []}', encoding="utf-8")

        yarim_video = self.isleniyor / "ders.mov"
        yarim_video.write_text("sahte mov")

        izle._geri_al(self.isleniyor, self.gelen, self.bitti)

        self.assertTrue((self.gelen / "ders.mov").exists())
        self.assertTrue(bitmis_bitti.exists())
        self.assertTrue((bitmis_bitti / "ders.srt").exists())
        self.assertTrue((bitmis_bitti / "ders.json").exists())

    def test_geri_al_yeni_ekli_bitmis_klasoru_korur(self):
        """Yeni <ad>_islenmis bitti klasörü, iş geri alındığında korunmalıdır."""
        bitmis_bitti = self.bitti / "ders_islenmis"
        bitmis_bitti.mkdir()
        (bitmis_bitti / "ders.srt").write_text("transkript", encoding="utf-8")
        yarim_video = self.isleniyor / "ders.mp4"
        yarim_video.write_text("sahte video")

        izle._geri_al(self.isleniyor, self.gelen, self.bitti)

        self.assertTrue(bitmis_bitti.exists())


    def test_geri_al_bos_veya_isaretli_yarim_klasoru_siler(self):
        """İçinde çıktı olmayan boş klasör veya yarım işaret dosyası taşıyan klasör silinir."""
        yarim_video = self.isleniyor / "ders.mp4"
        yarim_video.write_text("sahte video")

        bos_bitti = self.bitti / "ders"
        bos_bitti.mkdir()

        izle._geri_al(self.isleniyor, self.gelen, self.bitti)
        self.assertFalse(bos_bitti.exists())

        yarim_video2 = self.isleniyor / "ders2.mp4"
        yarim_video2.write_text("sahte video 2")
        isaretli_bitti = self.bitti / "ders2"
        isaretli_bitti.mkdir()
        (isaretli_bitti / ".yarim").touch()

        izle._geri_al(self.isleniyor, self.gelen, self.bitti)
        self.assertFalse(isaretli_bitti.exists())


class TestIzleCikisKlasoru(unittest.TestCase):
    def test_tek_tur_yeni_eki_ve_video_adini_korur(self):
        kok = Path(tempfile.mkdtemp())
        try:
            dizinler = {ad: kok / "is" / ad for ad in ("gelen", "isleniyor", "bitti", "hata")}
            for dizin in dizinler.values():
                dizin.mkdir(parents=True, exist_ok=True)
            (kok / "is").mkdir(exist_ok=True)
            video = dizinler["gelen"] / "ders.mp4"
            video.write_bytes(b"video")
            durum = video.stat()
            gorulen = {video: (durum.st_size, durum.st_mtime, 0)}
            ayar = type("AyarSahte", (), {"izle": type("IzleSahte", (), {"sabit_tur": 1})()})()

            def sahte_isle(video_yolu, cikis, _ayar, _bayraklar):
                (cikis / f"{video_yolu.stem}_islenmis.mp4").write_bytes(b"son video")
                return Sonuc(ad=video_yolu.stem, basarili=True, hata=None)

            with patch.object(izle.yollar, "is_dizini", side_effect=lambda ad: dizinler[ad]), \
                 patch.object(izle.yollar, "kok", return_value=kok), \
                 patch.object(izle, "isle_tek", side_effect=sahte_isle):
                islenen = izle.tek_tur(ayar, gorulen, Bayraklar())

            cikis = dizinler["bitti"] / "ders_islenmis"
            self.assertEqual(islenen, 1)
            self.assertTrue((cikis / "ders_islenmis.mp4").exists())
            self.assertTrue((cikis / "ders.mp4").exists())
        finally:
            shutil.rmtree(kok, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
