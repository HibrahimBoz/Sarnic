from pathlib import Path
import sqlite3
from unittest.mock import patch

from belge_destek import GeciciTest
from videohat.dizin import vt
from videohat.dizin.tarayici import tara
from videohat.dizin.yayin import Yayinci
from videohat.hatalar import OrtamHatasi


class YayinTest(GeciciTest):
    def setUp(self):
        super().setUp()
        self.agac = self.kok / "agac"
        self.agac.mkdir()
        self.yol = self.agac / "ışık.txt"
        self.yol.write_text("metin", encoding="utf-8")
        self.db = vt.ac(self.kok / "yerel" / "calisma.db")
        self.addCleanup(self.db.close)
        self.depo = self.kok / "depo"
        tara(self.db, [self.agac])
        self.yayinci = Yayinci(self.db, self.depo, 3)
        self.addCleanup(self.yayinci.kapat)

    def degistir(self, i):
        self.db.execute("INSERT OR REPLACE INTO meta(anahtar,deger) VALUES (?,?)", ("test", str(i)))
        self.db.commit()

    def test_yayin_gecerli_fts_ve_degismeyince_atla(self):
        id_ = self.db.execute("SELECT id FROM dosya").fetchone()[0]
        vt.sayfalari_yaz(self.db, id_, [{"no": 1, "etiket": "Sayfa 1", "kaynak": "metin", "metin": "Işık ﬁkri güvenlik"}])
        p = self.yayinci.yayinla()
        self.assertEqual((self.depo / "yayin" / "guncel.txt").read_text(encoding="utf-8"), p.name + "\n")
        d = sqlite3.connect(p)
        try:
            self.assertEqual(d.execute("PRAGMA quick_check").fetchone()[0], "ok")
            self.assertEqual(d.execute("SELECT count(*) FROM dosya_fts WHERE dosya_fts MATCH ?", ("isik",)).fetchone()[0], 1)
            self.assertEqual(d.execute("SELECT count(*) FROM sayfa_fts WHERE sayfa_fts MATCH ?", ("guvenlik",)).fetchone()[0], 1)
            self.assertEqual(d.execute("SELECT metin FROM sayfa").fetchone()[0], "Işık fikri güvenlik")
        finally:
            d.close()
        self.assertIsNone(self.yayinci.yayinla())
        tara(self.db, [self.agac])
        self.assertIsNone(self.yayinci.yayinla())

    def test_sakla_yayin_eski_kilitliyse_sessiz(self):
        for i in range(5):
            self.degistir(i)
            self.yayinci.yayinla()
        dosyalar = sorted((self.depo / "yayin").glob("*.db"))
        self.assertEqual(len(dosyalar), 3)
        kilitli = dosyalar[0]
        gercek = Path.unlink

        def sil(p, *a, **kw):
            if p == kilitli: raise PermissionError("kilitli")
            return gercek(p, *a, **kw)

        with patch.object(Path, "unlink", sil):
            self.degistir(9)
            self.yayinci.yayinla()
        self.assertTrue(kilitli.exists())
        self.degistir(10)
        self.yayinci.yayinla()
        self.assertEqual(len(list((self.depo / "yayin").glob("*.db"))), 3)

    def test_kopya_yerel_atomik_ve_ag_hatasi_eski_isareti_korur(self):
        ilk = self.yayinci.yayinla()
        self.degistir(2)
        with patch("videohat.dizin.yayin.shutil.copyfile", side_effect=OSError("ağ kesildi")) as kopya:
            with self.assertRaises(OSError):
                self.yayinci.yayinla()
        kaynak, hedef = kopya.call_args.args
        self.assertEqual(Path(kaynak).parent.parent, self.kok / "yerel")
        self.assertTrue(str(hedef).endswith(".db.tmp"))
        self.assertEqual((self.depo / "yayin" / "guncel.txt").read_text(encoding="utf-8").strip(), ilk.name)
        sonraki = self.yayinci.yayinla()
        self.assertNotEqual(ilk, sonraki)
        self.assertFalse(list((self.depo / "yayin").glob("*.tmp")))

    def test_son_surum_varken_ozet_hesaplanmaz(self):
        # 15. bulgu: self.son_surum is not None iken her yayında _ozet hesaplanmaz
        ilk = self.yayinci.yayinla()
        self.assertIsNotNone(self.yayinci.son_surum)
        self.degistir(55)
        with patch.object(Yayinci, "_ozet", side_effect=AssertionError("Gereksiz özet hesaplandı")):
            yeni = self.yayinci.yayinla()
            self.assertIsNotNone(yeni)

    def test_sema_aynen_yuklenir(self):
        self.assertEqual(self.db.execute("PRAGMA foreign_keys").fetchone()[0], 1)
        self.assertEqual(self.db.execute("PRAGMA journal_mode").fetchone()[0], "wal")
        self.assertEqual(self.db.execute("SELECT deger FROM meta WHERE anahtar=?", ("surum",)).fetchone()[0], "1")
        fts = self.db.execute("SELECT sql FROM sqlite_master WHERE name=?", ("sayfa_fts",)).fetchone()[0]
        self.assertIn("contentless_delete=1", fts)

    def test_tek_yazar_kilidi(self):
        p = self.kok / "kilitli" / "dizin.db"
        with vt.yazar_kilidi(p):
            with self.assertRaisesRegex(OrtamHatasi, "başka bir yazar"):
                with vt.yazar_kilidi(p):
                    self.fail("İkinci yazar açıldı")
        with vt.yazar_kilidi(p):
            pass

    def test_yeniden_baslatmada_degismeyen_yayin_atlanir(self):
        ilk = self.yayinci.yayinla()
        with Yayinci(self.db, self.depo, 3) as yeni:
            self.assertIsNone(yeni.yayinla())
            self.degistir(99)
            self.assertNotEqual(yeni.yayinla(), ilk)
