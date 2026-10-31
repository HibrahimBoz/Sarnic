"""Dizin çeviri desteği testleri (Görev 4)."""
import json
import sqlite3
import unittest
from unittest.mock import Mock, patch
from pathlib import Path

from belge_destek import GeciciTest
from sarnicatolye.belge.onbellek import json_yaz
from sarnicatolye.dizin import vt
from sarnicatolye.dizin.dizinle import ceviri_gecisi, icerikleri_isle
from sarnicatolye.dizin.normal import icerik_normalle


class DizinCeviriTest(GeciciTest):
    """Görev 4 dizin ve çeviri entegrasyon testleri."""

    def test_eski_semali_dizin_acilinca_sutunlar_eklenir(self):
        """Eski şemalı dizin açılınca dil ve ceviri sütunları ile sayfa_ceviri_fts eklenir."""
        db_yol = self.kok / "eski_dizin.db"
        conn = sqlite3.connect(db_yol)
        conn.executescript("""
            CREATE TABLE meta(anahtar TEXT PRIMARY KEY, deger TEXT NOT NULL);
            INSERT INTO meta(anahtar, deger) VALUES ('surum', '1');
            CREATE TABLE dosya(
                id INTEGER PRIMARY KEY, yol TEXT NOT NULL UNIQUE, ad TEXT NOT NULL,
                klasor TEXT NOT NULL, uzanti TEXT NOT NULL, boyut INTEGER NOT NULL,
                degisme INTEGER NOT NULL, tur TEXT NOT NULL, durum TEXT NOT NULL,
                hata TEXT, sha TEXT, sayfa_sayisi INTEGER, ocr_sayfa INTEGER,
                ders TEXT, norm_ad TEXT NOT NULL, norm_yol TEXT NOT NULL
            );
            CREATE TABLE sayfa(
                id INTEGER PRIMARY KEY, dosya_id INTEGER NOT NULL REFERENCES dosya(id) ON DELETE CASCADE,
                no INTEGER NOT NULL, etiket TEXT NOT NULL, kaynak TEXT NOT NULL,
                guven REAL, metin TEXT NOT NULL
            );
            CREATE VIRTUAL TABLE sayfa_fts USING fts5(
                norm, content='', contentless_delete=1,
                tokenize='unicode61 remove_diacritics 0', prefix='2 3'
            );
            CREATE TRIGGER sayfa_ad AFTER DELETE ON sayfa BEGIN
                DELETE FROM sayfa_fts WHERE rowid = old.id;
            END;
        """)
        # Başlangıçta sütunlar ve sayfa_ceviri_fts yok
        eski_sutunlar = {r[1] for r in conn.execute("PRAGMA table_info(sayfa)").fetchall()}
        self.assertNotIn("dil", eski_sutunlar)
        self.assertNotIn("ceviri", eski_sutunlar)
        conn.close()

        # vt.ac çağrısıyla göç çalışır
        db = vt.ac(db_yol)
        try:
            yeni_sutunlar = {r["name"] for r in db.execute("PRAGMA table_info(sayfa)").fetchall()}
            self.assertIn("dil", yeni_sutunlar)
            self.assertIn("ceviri", yeni_sutunlar)

            # sayfa_ceviri_fts sanal tablosu oluşturulmuş mu?
            fts_tablo = db.execute(
                "SELECT name FROM sqlite_master WHERE type='table' AND name='sayfa_ceviri_fts'"
            ).fetchone()
            self.assertIsNotNone(fts_tablo)

            # sayfa_ad tetikleyicisi güncellenmiş mi?
            tetikleyici = db.execute(
                "SELECT sql FROM sqlite_master WHERE type='trigger' AND name='sayfa_ad'"
            ).fetchone()[0]
            self.assertIn("sayfa_fts", tetikleyici)
            self.assertIn("sayfa_ceviri_fts", tetikleyici)
        finally:
            db.close()

    def test_tetikleyici_gocu_yalniz_bir_kez_yapilir(self):
        # İnceleme bulgusu: her açılışta DROP+CREATE yapılıyordu (atomik değil, gereksiz şema yazımı).
        db_yol = self.kok / "dizin_tetik.db"
        db = vt.ac(db_yol)
        db.close()
        db = vt.ac(db_yol)
        try:
            ilk = db.execute("PRAGMA schema_version").fetchone()[0]
        finally:
            db.close()
        db = vt.ac(db_yol)
        try:
            self.assertEqual(db.execute("PRAGMA schema_version").fetchone()[0], ilk)
            sql = db.execute("SELECT sql FROM sqlite_master WHERE name='sayfa_ad'").fetchone()[0]
            self.assertIn("sayfa_ceviri_fts", sql)
        finally:
            db.close()

    def test_toplanti_sorgusu_sayfa_ceviri_ftsde_rusca_sayfayi_bulur(self):
        """Toplanti sorgusu sayfa_ceviri_fts'de Rusça sayfayı bulur."""
        db_yol = self.kok / "dizin.db"
        db = vt.ac(db_yol)
        try:
            cur = db.execute(
                "INSERT INTO dosya(yol,ad,klasor,uzanti,boyut,degisme,tur,durum,sha,sayfa_sayisi,ocr_sayfa,norm_ad,norm_yol) "
                "VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?)",
                ("C:/belgeler/rusca.pdf", "rusca.pdf", "C:/belgeler", "pdf", 2048, 1700000000,
                 "belge", "tamam", "shatest1", 1, 0, "rusca.pdf", "c:/belgeler/rusca.pdf")
            )
            dosya_id = cur.lastrowid
            sayfalar = [{
                "no": 1,
                "etiket": "Sayfa 1",
                "kaynak": "metin",
                "metin": "Встреча назначена на завтра",
                "dil": "ru",
                "ceviri": "Toplantı yarına planlandı"
            }]
            vt.sayfalari_yaz(db, dosya_id, sayfalar)

            # Özgün FTS'de 'toplanti' bulunamaz
            ozgun_bulunan = db.execute("SELECT rowid FROM sayfa_fts WHERE sayfa_fts MATCH 'toplanti'").fetchall()
            self.assertEqual(len(ozgun_bulunan), 0)

            # Çeviri FTS'de 'toplanti' bulunur
            ceviri_bulunan = db.execute("SELECT rowid FROM sayfa_ceviri_fts WHERE sayfa_ceviri_fts MATCH 'toplanti'").fetchall()
            self.assertEqual(len(ceviri_bulunan), 1)

            # Eşleşen satır sayfa tablosundaki Rusça sayfadır
            sayfa_kaydi = db.execute("SELECT * FROM sayfa WHERE id=?", (ceviri_bulunan[0]["rowid"],)).fetchone()
            self.assertEqual(sayfa_kaydi["dil"], "ru")
            self.assertEqual(sayfa_kaydi["metin"], "Встреча назначена на завтра")
            self.assertEqual(sayfa_kaydi["ceviri"], "Toplantı yarına planlandı")
        finally:
            db.close()

    def test_sayfa_silinince_iki_ftsden_de_silinir(self):
        """Sayfa silinince sayfa_ad tetikleyicisi hem sayfa_fts hem de sayfa_ceviri_fts'den siler."""
        db_yol = self.kok / "dizin.db"
        db = vt.ac(db_yol)
        try:
            cur = db.execute(
                "INSERT INTO dosya(yol,ad,klasor,uzanti,boyut,degisme,tur,durum,sha,sayfa_sayisi,ocr_sayfa,norm_ad,norm_yol) "
                "VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?)",
                ("C:/belgeler/silinecek.pdf", "silinecek.pdf", "C:/belgeler", "pdf", 1024, 1700000000,
                 "belge", "tamam", "shatest2", 1, 0, "silinecek.pdf", "c:/belgeler/silinecek.pdf")
            )
            dosya_id = cur.lastrowid
            vt.sayfalari_yaz(db, dosya_id, [{
                "no": 1,
                "etiket": "Sayfa 1",
                "kaynak": "metin",
                "metin": "Özgün metin",
                "dil": "ru",
                "ceviri": "Türkçe çeviri metni"
            }])
            sayfa_id = db.execute("SELECT id FROM sayfa WHERE dosya_id=?", (dosya_id,)).fetchone()["id"]

            # İki FTS'de de kayıt mevcut
            self.assertEqual(db.execute("SELECT count(*) FROM sayfa_fts WHERE rowid=?", (sayfa_id,)).fetchone()[0], 1)
            self.assertEqual(db.execute("SELECT count(*) FROM sayfa_ceviri_fts WHERE rowid=?", (sayfa_id,)).fetchone()[0], 1)

            # Sayfayı sil
            db.execute("DELETE FROM sayfa WHERE id=?", (sayfa_id,))

            # İki FTS'den de silindi
            self.assertEqual(db.execute("SELECT count(*) FROM sayfa_fts WHERE rowid=?", (sayfa_id,)).fetchone()[0], 0)
            self.assertEqual(db.execute("SELECT count(*) FROM sayfa_ceviri_fts WHERE rowid=?", (sayfa_id,)).fetchone()[0], 0)
        finally:
            db.close()

    def test_ceviri_gecisi_zaten_islenmis_belgeyi_yeniden_ocrlamaz(self):
        """Çeviri geçişi zaten işlenmiş belgeyi yeniden OCR'lamaz; belge.json'dan çeviri ekler."""
        db_yol = self.kok / "dizin.db"
        depo = self.kok / "_sarnic"
        db = vt.ac(db_yol)
        try:
            sha = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
            cur = db.execute(
                "INSERT INTO dosya(yol,ad,klasor,uzanti,boyut,degisme,tur,durum,sha,sayfa_sayisi,ocr_sayfa,norm_ad,norm_yol) "
                "VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?)",
                ("C:/belgeler/onceden_islenmis.pdf", "onceden_islenmis.pdf", "C:/belgeler", "pdf",
                 2048, 1700000000, "belge", "tamam", sha, 1, 1, "onceden_islenmis.pdf", "c:/belgeler/onceden_islenmis.pdf")
            )
            dosya_id = cur.lastrowid
            # Eski format: sayfada dil ve ceviri henüz NULL
            cur_s = db.execute(
                "INSERT INTO sayfa(dosya_id,no,etiket,kaynak,guven,metin,dil,ceviri) VALUES (?,?,?,?,?,?,?,?)",
                (dosya_id, 1, "Sayfa 1", "ocr", 0.95, "Встреча назначена на завтра", None, None)
            )
            db.commit()

            # Depoda belge.json mevcut
            hedef = depo / "belgeler" / sha[:2] / sha
            hedef.mkdir(parents=True, exist_ok=True)
            veri = {
                "surum": 1,
                "durum": "tamam",
                "sha": sha,
                "kaynak_ad": "onceden_islenmis.pdf",
                "bicim": "pdf",
                "sayfa_sayisi": 1,
                "ocr_sayfa": 1,
                "sayfalar": [{
                    "no": 1,
                    "etiket": "Sayfa 1",
                    "kaynak": "ocr",
                    "guven": 0.95,
                    "metin": "Встреча назначена на завтра"
                }]
            }
            json_yaz(hedef / "belge.json", veri)

            def sahte_ceviri_ekle(v, cevirmen):
                for s in v.get("sayfalar", []):
                    s["dil"] = "ru"
                    s["ceviri"] = "Toplantı yarına planlandı"
                v["ceviri_surum"] = 1
                return True

            with patch("sarnicatolye.dizin.dizinle.isle_belge") as mock_isle_belge, \
                 patch("sarnicatolye.dizin.dizinle.ceviri_ekle", side_effect=sahte_ceviri_ekle):
                islenen = ceviri_gecisi(db, depo, self.ayar, cevirmen=object())

            # isle_belge (OCR çıkarma) kesinlikle çağrılmadı
            mock_isle_belge.assert_not_called()
            self.assertEqual(islenen, 1)

            # Veritabanında sayfa güncellendi
            sayfa_kaydi = db.execute("SELECT * FROM sayfa WHERE dosya_id=?", (dosya_id,)).fetchone()
            self.assertEqual(sayfa_kaydi["dil"], "ru")
            self.assertEqual(sayfa_kaydi["ceviri"], "Toplantı yarına planlandı")

            # sayfa_ceviri_fts tablosunda aranabilir hale geldi
            bulunan = db.execute("SELECT rowid FROM sayfa_ceviri_fts WHERE sayfa_ceviri_fts MATCH 'toplanti'").fetchall()
            self.assertEqual(len(bulunan), 1)

            # belge.json güncellendi
            guncel_veri = json.loads((hedef / "belge.json").read_text(encoding="utf-8"))
            self.assertEqual(guncel_veri.get("ceviri_surum"), 1)
            self.assertEqual(guncel_veri["sayfalar"][0].get("dil"), "ru")
        finally:
            db.close()

    def test_ceviri_gecisi_belge_json_yazilip_kesilen_surec_kurtulur(self):
        """belge.json yazılıp commit'ten önce kesilen süreçte ceviri_ekle False dönse de sayfalari_yaz çalışır."""
        db_yol = self.kok / "dizin.db"
        depo = self.kok / "_sarnic"
        db = vt.ac(db_yol)
        try:
            sha = "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef"
            cur = db.execute(
                "INSERT INTO dosya(yol,ad,klasor,uzanti,boyut,degisme,tur,durum,sha,sayfa_sayisi,ocr_sayfa,norm_ad,norm_yol) "
                "VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?)",
                ("C:/belgeler/kesilen.pdf", "kesilen.pdf", "C:/belgeler", "pdf",
                 2048, 1700000000, "belge", "tamam", sha, 1, 0, "kesilen.pdf", "c:/belgeler/kesilen.pdf")
            )
            dosya_id = cur.lastrowid
            # Veritabanında dil hala NULL
            db.execute(
                "INSERT INTO sayfa(dosya_id,no,etiket,kaynak,guven,metin,dil,ceviri) VALUES (?,?,?,?,?,?,?,?)",
                (dosya_id, 1, "Sayfa 1", "metin", None, "Встреча назначена на завтра", None, None)
            )
            db.commit()

            # belge.json ise önceden yazılmış durumda (ceviri_ekle değişmedi sayar / False döner)
            hedef = depo / "belgeler" / sha[:2] / sha
            hedef.mkdir(parents=True, exist_ok=True)
            veri = {
                "surum": 1,
                "durum": "tamam",
                "sha": sha,
                "kaynak_ad": "kesilen.pdf",
                "sayfa_sayisi": 1,
                "ocr_sayfa": 0,
                "ceviri_surum": 1,
                "sayfalar": [{
                    "no": 1,
                    "etiket": "Sayfa 1",
                    "kaynak": "metin",
                    "metin": "Встреча назначена на завтра",
                    "dil": "ru",
                    "ceviri": "Toplantı yarına planlandı"
                }]
            }
            json_yaz(hedef / "belge.json", veri)

            def degismeyen_ceviri_ekle(v, cevirmen):
                # Zaten ceviri_surum var, değişiklik yapmadı
                return False

            with patch("sarnicatolye.dizin.dizinle.ceviri_ekle", side_effect=degismeyen_ceviri_ekle):
                ceviri_gecisi(db, depo, self.ayar, cevirmen=object())

            # Değişmese de sayfalari_yaz çağrılmış ve veritabanı kurtarılmıştır
            sayfa_kaydi = db.execute("SELECT * FROM sayfa WHERE dosya_id=?", (dosya_id,)).fetchone()
            self.assertEqual(sayfa_kaydi["dil"], "ru")
            self.assertEqual(sayfa_kaydi["ceviri"], "Toplantı yarına planlandı")

            # sayfa_ceviri_fts tablosunda aranabilir hale geldi
            bulunan = db.execute("SELECT rowid FROM sayfa_ceviri_fts WHERE sayfa_ceviri_fts MATCH 'toplanti'").fetchall()
            self.assertEqual(len(bulunan), 1)
        finally:
            db.close()


if __name__ == "__main__":
    unittest.main()
