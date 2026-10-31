import os
import unittest
from pathlib import Path
from unittest.mock import patch

from belge_destek import GeciciTest, SahteHavuz
from videohat.dizin import vt
from videohat.dizin.tarayici import tara
from videohat.dizin.dizinle import icerikleri_isle, calistir


class TaramaTest(GeciciTest):
    def setUp(self):
        super().setUp()
        self.agac = self.kok / "agac"
        self.agac.mkdir()
        self.depo = self.kok / "depo"
        self.db = vt.ac(self.kok / "yerel" / "dizin.db")
        self.addCleanup(self.db.close)

    def yaz(self, ad, metin="İçerik ölçüm"):
        p = self.agac / ad
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(metin, encoding="utf-8")
        return p

    def tara(self, **kw):
        return tara(self.db, [self.agac], self.ayar.dizin.haric, **kw)

    def kayit(self, yol):
        return self.db.execute("SELECT * FROM dosya WHERE yol=?", (str(yol),)).fetchone()

    def isle(self):
        return icerikleri_isle(self.db, self.depo, self.ayar, SahteHavuz())

    def test_turler_haric_ve_ders_baglari(self):
        dosyalar = [self.yaz("a.txt"), self.yaz("b.png"), self.yaz("c.mp4"), self.yaz("d.bin")]
        self.yaz(".GIT/gizli.txt")
        self.yaz("_sarnic/gizli.txt")
        self.yaz("_veridedektifi/gizli.txt")
        self.yaz("_bulgu/gizli.txt")
        bagli = [self.yaz("ders_islenmis/dosya.txt"), self.yaz("eski_videoperisi/dosya.txt"),
                 self.yaz("ders_belgeler/belge.txt"), self.yaz("ders.mp4")]
        s = self.tara()
        self.assertTrue(s.yayin_uygun)
        self.assertEqual(s.gorulen, 11)
        self.assertEqual([self.kayit(p)["tur"] for p in dosyalar], ["belge", "goruntu", "video", "diger"])
        self.assertEqual([self.kayit(p)["durum"] for p in dosyalar], ["bekliyor", "bekliyor", "yok", "yok"])
        self.assertEqual(self.kayit(self.agac / "ders_islenmis")["tur"], "klasor")
        self.assertEqual([self.kayit(p)["ders"] for p in bagli],
                         [str(self.agac / "ders_islenmis"), str(self.agac / "eski_videoperisi"),
                          str(self.agac / "ders_islenmis"), str(self.agac / "ders_islenmis")])
        self.assertIsNone(self.kayit(self.agac / ".GIT/gizli.txt"))
        self.assertIsNone(self.kayit(self.agac / "_sarnic"))
        self.assertIsNone(self.kayit(self.agac / "_veridedektifi"))
        self.assertIsNone(self.kayit(self.agac / "_bulgu"))

    def test_haric_korunana_eklenmez(self):
        # 9. bulgu: haric klasörleri korunan tablosuna eklenmez
        self.yaz(".git/config", "repo")
        self.yaz("node_modules/pkg/index.js", "js")
        self.tara()
        korunan_sayisi = self.db.execute("SELECT count(*) FROM korunan").fetchone()[0]
        self.assertEqual(korunan_sayisi, 0)

    def test_ag_kopmasinda_kok_dogrulama_ve_yayin_koruma(self):
        # 4(a) bulgu: alt klasörde OSError olunca kök erişimi yeniden doğrulanır, ağ kopmuşsa yayin_uygun False olur
        alt = self.yaz("alt/dosya.txt")
        gercek_scandir = os.scandir

        def sahte_scandir(p):
            if Path(p) == alt.parent:
                raise OSError("ağ koptu")
            return gercek_scandir(p)

        with patch("videohat.dizin.tarayici.os.scandir", side_effect=sahte_scandir), \
                patch("videohat.dizin.tarayici._kok_erisim_kontrol", return_value=False):
            sonuc = self.tara()
            self.assertFalse(sonuc.yayin_uygun)

    def test_kanonik_yol_ve_buyuk_kucuk_harf_eslesmesi(self):
        # 4(b) bulgu: kök veya dosya harf büyüklüğü değişirse çift kayıt açılmaz, sayfalar silinmez
        yol = self.yaz("ışık.txt", "içerik")
        self.tara()
        self.isle()
        id1 = self.kayit(yol)["id"]
        kok_farkli = Path(str(self.agac).upper())
        tara(self.db, [kok_farkli], self.ayar.dizin.haric)
        id2 = self.db.execute("SELECT id FROM dosya WHERE kanon(yol)=kanon(?)", (str(yol),)).fetchone()[0]
        self.assertEqual(id1, id2)
        toplam = self.db.execute("SELECT count(*) FROM dosya").fetchone()[0]
        self.assertEqual(toplam, 1)

    def test_dongu_yayin_hatasinda_cokmez(self):
        # 6. bulgu: _dongu içindeki yayinla() çağrıları ağ hatasında süreci çökertmez
        self.yaz("a.txt")
        self.ayar.dizin.calisma_db = str(self.kok / "yerel" / "dizin.db")
        with patch("videohat.dizin.yayin.Yayinci.yayinla", side_effect=OSError("ağ hatası")):
            kod = calistir(self.ayar, [self.agac], self.depo, bir_kez=True)
            self.assertIn(kod, (0, 1))

    def test_degisen_belge_sayfa_fts_temizligi_silme(self):
        yol = self.yaz("ışık.txt", "Eski içerik güvenlik")
        self.yaz("kalacak.bin")
        self.tara()
        self.assertEqual(self.isle(), 0)
        id_ = self.kayit(yol)["id"]
        self.assertEqual(self.kayit(yol)["durum"], "tamam")
        self.assertEqual(self.db.execute("SELECT count(*) FROM dosya_fts WHERE dosya_fts MATCH ?", ('"isik"',)).fetchone()[0], 1)
        self.assertEqual(self.db.execute("SELECT count(*) FROM sayfa_fts WHERE sayfa_fts MATCH ?", ('"guvenlik"',)).fetchone()[0], 1)
        yol.write_text("Yeni ve daha uzun frekans ölçümü içeriği", encoding="utf-8")
        self.tara()
        self.assertEqual(self.kayit(yol)["durum"], "bekliyor")
        self.assertEqual(self.db.execute("SELECT count(*) FROM sayfa WHERE dosya_id=?", (id_,)).fetchone()[0], 0)
        self.assertEqual(self.db.execute("SELECT count(*) FROM sayfa_fts WHERE sayfa_fts MATCH ?", ("guvenlik",)).fetchone()[0], 0)
        self.isle()
        yol.unlink()
        self.assertEqual(self.tara().silinen, 1)
        self.assertIsNone(self.kayit(yol))
        self.assertEqual(self.db.execute("SELECT count(*) FROM sayfa").fetchone()[0], 0)
        self.assertEqual(self.db.execute("SELECT count(*) FROM sayfa_fts WHERE sayfa_fts MATCH ?", ("frekans",)).fetchone()[0], 0)

    def test_ayni_icerik_tek_belge_json_iki_sayfa(self):
        self.yaz("a.txt")
        self.yaz("b.txt")
        self.tara()
        self.isle()
        self.assertEqual(len(list(self.depo.rglob("belge.json"))), 1)
        self.assertEqual(self.db.execute("SELECT count(*) FROM sayfa").fetchone()[0], 2)
        self.assertEqual(self.db.execute("SELECT count(DISTINCT sha) FROM dosya").fetchone()[0], 1)

    def test_erisilemeyen_dal_ve_stat_hatasi_silinmez(self):
        korunan = self.yaz("koru/a.txt")
        kalacak = self.yaz("stat.txt")
        silinecek = self.yaz("sil.txt")
        self.yaz("kal.bin")
        self.tara()
        silinecek.unlink()
        gercek = os.scandir

        class Girdi:
            def __init__(self, e): self.e = e
            def __getattr__(self, ad): return getattr(self.e, ad)
            def stat(self, **kw):
                if self.e.name == "stat.txt": raise PermissionError("stat kapalı")
                return self.e.stat(**kw)

        class Girdiler:
            def __init__(self, p): self.it = gercek(p)
            def __iter__(self): return self
            def __next__(self): return Girdi(next(self.it))
            def close(self): self.it.close()
            def __enter__(self): return self
            def __exit__(self, *args): self.close()

        def tara_sahte(p):
            if Path(p).name == "koru": raise PermissionError("paylaşım kapalı")
            return Girdiler(p)

        with patch("videohat.dizin.tarayici.os.scandir", side_effect=tara_sahte):
            self.assertEqual(self.tara().silinen, 1)
        self.assertIsNotNone(self.kayit(korunan))
        self.assertIsNotNone(self.kayit(kalacak))

    def test_kok_kapali_yayin_yok_yuzde50_ve_zorla(self):
        dosyalar = [self.yaz(f"{i}.txt") for i in range(4)]
        self.tara()
        with patch("videohat.dizin.tarayici.os.scandir", side_effect=PermissionError("ağ yok")):
            s = self.tara()
        self.assertFalse(s.yayin_uygun)
        self.assertEqual(self.db.execute("SELECT count(*) FROM dosya").fetchone()[0], 4)
        for p in dosyalar[:3]: p.unlink()
        self.assertEqual(self.tara().silinen, 0)
        self.assertEqual(self.tara(zorla_sil=True).silinen, 3)

    def test_cok_kok_tamamlanmadan_silme_yok_ve_bekleyen_korunur(self):
        sil = self.yaz("sil.txt")
        diger = self.kok / "ikinci"
        diger.mkdir()
        bekleyen = diger / "bekleyen.txt"
        bekleyen.write_text("bekleyen", encoding="utf-8")
        tara(self.db, [self.agac, diger])
        sil.unlink()
        gercek = os.scandir

        def bak(p):
            if Path(p) == diger:
                self.assertIsNotNone(self.kayit(sil))
                raise PermissionError("ikinci kök kapalı")
            return gercek(p)

        with patch("videohat.dizin.tarayici.os.scandir", side_effect=bak):
            s = tara(self.db, [self.agac, diger], zorla_sil=True)
        self.assertFalse(s.yayin_uygun)
        self.assertIsNone(self.kayit(sil))
        with patch("videohat.dizin.dizinle.isle_belge", side_effect=AssertionError("KAPALI KÖK İŞLENDİ")):
            self.isle()
        self.assertEqual(self.kayit(bekleyen)["durum"], "bekliyor")

    def test_1000_satirlik_commit_ve_agac_bellekte_yok(self):
        for i in range(1005):
            self.yaz(f"{i}.bin", "")
        ifadeler = []
        self.db.set_trace_callback(lambda s: ifadeler.append(s) if s == "COMMIT" else None)
        sonuc = self.tara()
        self.db.set_trace_callback(None)
        self.assertEqual(sonuc.gorulen, 1005)
        self.assertGreaterEqual(len(ifadeler), 2)
        self.assertEqual(self.tara().degisen, 0)

    def test_buyuk_desteklenmeyen_hata_ve_degismeyen_tekrar_yok(self):
        buyuk = self.yaz("buyuk.txt")
        eski = self.yaz("eski.doc")
        bozuk = self.yaz("bozuk.pdf", "PDF DEĞİL")
        self.ayar.dizin.max_dosya_mb = 1
        with buyuk.open("wb") as f: f.truncate(2 * 1024 * 1024)
        self.tara()
        with patch("videohat.belge.ofis.yollar.libreoffice", return_value=None), \
                patch("videohat.belge.ofis.yollar.artcraft_cli", return_value=None):
            self.assertEqual(self.isle(), 1)
        self.assertEqual(self.kayit(buyuk)["hata"], "Dosya çok büyük")
        self.assertEqual(self.kayit(eski)["durum"], "desteklenmiyor")
        self.assertEqual(self.kayit(bozuk)["durum"], "hata")
        self.tara()
        with patch("videohat.dizin.dizinle.isle_belge", side_effect=AssertionError("TEKRAR")):
            self.assertEqual(self.isle(), 0)

    def test_katalog_icerikten_once_yayin_ve_kok_hatasi(self):
        self.yaz("a.txt")
        self.ayar.dizin.calisma_db = str(self.kok / "baska" / "dizin.db")
        durumlar = []

        def yayinla(yayinci):
            durumlar.append(yayinci.db.execute("SELECT durum FROM dosya WHERE tur=?", ("belge",)).fetchone()[0])

        with patch("videohat.dizin.yayin.Yayinci.yayinla", autospec=True, side_effect=yayinla):
            self.assertEqual(calistir(self.ayar, [self.agac], self.depo, bir_kez=True), 0)
        self.assertEqual(durumlar, ["bekliyor", "tamam"])
        with patch("videohat.dizin.tarayici.os.scandir", side_effect=PermissionError("ağ")), \
                patch("videohat.dizin.yayin.Yayinci.yayinla") as yayin:
            calistir(self.ayar, [self.agac], self.depo, bir_kez=True, yalniz_ad=True)
        yayin.assert_not_called()


class TranskriptTurTest(unittest.TestCase):
    def test_ders_klasorundeki_transkript_belge_sayilmaz(self):
        from videohat.dizin.tarayici import tur_bul
        ders = Path("D:/k/ders1_islenmis")
        for ad in ("ders1.json", "ders1.srt", "ders1.vtt", "ders1.txt"):
            self.assertEqual(tur_bul(ders / ad), "diger")
        self.assertEqual(tur_bul(ders / "belgeler" / "notlar.txt"), "belge")  # alt klasördeki belge işlenir
        self.assertEqual(tur_bul(ders / "sunum.pdf"), "belge")
        self.assertEqual(tur_bul(Path("D:/k/notlar.txt")), "belge")
