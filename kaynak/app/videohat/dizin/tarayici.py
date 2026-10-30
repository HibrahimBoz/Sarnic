"""Akış halinde katalog taraması; erişilemeyen dallarda silme koruması."""
from dataclasses import dataclass
import logging
import os
from pathlib import Path

from ..belge import BELGE_UZANTILARI, GORUNTU_UZANTILARI
from .normal import dosya_normalle
from .vt import _kanon

log = logging.getLogger("videohat")
VIDEO = frozenset(".mp4 .mkv .mov .avi .m4a .wav .mp3".split())


@dataclass
class TaramaSonucu:
    gorulen: int = 0
    degisen: int = 0
    silinen: int = 0
    yayin_uygun: bool = True


TRANSKRIPT_UZANTILARI = {".json", ".srt", ".vtt", ".txt"}


def tur_bul(yol, klasor=False):
    if klasor:
        return "klasor"
    uz = yol.suffix.lower()
    # Ders çıktı klasörünün doğrudan içindeki transkript dosyaları (json/srt/vtt/txt) belge değildir:
    # içerikleri video aramasında zaten var; belge olarak işlenirse ham JSON sonuçlara karışır (ölçüldü).
    if (uz in TRANSKRIPT_UZANTILARI
            and yol.parent.name.lower().endswith(("_islenmis", "_videoperisi"))):
        return "diger"
    return ("goruntu" if uz in GORUNTU_UZANTILARI else "belge" if uz in BELGE_UZANTILARI
            else "video" if uz in VIDEO else "diger")


def ders_bul(yol):
    for ust in yol.parents:
        ad = ust.name.lower()
        if ad.endswith(("_islenmis", "_videoperisi")):
            return str(ust)
        if ad.endswith("_belgeler"):
            aday = ust.with_name(ust.name[:-9] + "_islenmis")
            if aday.is_dir():
                return str(aday)
    if yol.suffix.lower() in VIDEO:
        aday = yol.with_name(yol.stem + "_islenmis")
        if aday.is_dir():
            return str(aday)
    return None


def _alt_deseni(yol):
    return str(yol).rstrip("\\/").replace("!", "!!").replace("%", "!%").replace("_", "!_") + os.sep + "%"


def _kok_erisim_kontrol(kok):
    try:
        it = os.scandir(kok)
    except OSError:
        return False
    try:
        return True
    finally:
        if hasattr(it, "close"):
            it.close()


def tara(db, kokler, haric=(), zorla_sil=False, haric_yollar=()):
    sonuc = TaramaSonucu()
    haric = {s.casefold() for s in haric}
    haric_yollar = {os.path.normcase(os.path.abspath(p)) for p in haric_yollar}
    db.execute("CREATE TEMP TABLE IF NOT EXISTS gorulen(yol TEXT PRIMARY KEY)")
    db.execute("CREATE TEMP TABLE IF NOT EXISTS korunan(yol TEXT PRIMARY KEY, desen TEXT NOT NULL)")
    db.execute("DELETE FROM gorulen")
    db.execute("DELETE FROM korunan")

    def koru(yol, e):
        log.warning("Taranamadı, eski kayıtlar korundu: %s (%s)", yol, e)
        k_yol = _kanon(yol)
        db.execute("INSERT OR IGNORE INTO korunan(yol,desen) VALUES (?,?)", (k_yol, _alt_deseni(k_yol)))

    def satir(yol, st, klasor):
        s = str(yol)
        kanon_s = _kanon(s)
        db.execute("INSERT OR IGNORE INTO gorulen(yol) VALUES (?)", (kanon_s,))
        sonuc.gorulen += 1
        tur = tur_bul(yol, klasor)
        boyut = 0 if klasor else st.st_size
        degisme = int(st.st_mtime)
        ders = ders_bul(yol)
        eski = db.execute("SELECT id,yol,boyut,degisme,tur,ders FROM dosya WHERE yol=?", (s,)).fetchone()
        if eski is None:
            # İndeksli arama (her yeni dosyada tam tablo taraması olmasın; Argon 2. tur)
            eski = db.execute("SELECT id,yol,boyut,degisme,tur,ders FROM dosya WHERE yol=? COLLATE NOCASE",
                              (s,)).fetchone()
            if eski is not None and eski["yol"] != s:
                db.execute("UPDATE dosya SET yol=?, ad=?, klasor=?, norm_ad=?, norm_yol=? WHERE id=?",
                           (s, yol.name, str(yol.parent), dosya_normalle(yol.name), dosya_normalle(s), eski["id"]))
        durum = "bekliyor" if tur in ("belge", "goruntu") else "yok"
        if eski is None:
            db.execute("""INSERT INTO dosya(yol,ad,klasor,uzanti,boyut,degisme,tur,durum,ders,norm_ad,norm_yol)
                          VALUES (?,?,?,?,?,?,?,?,?,?,?)""",
                       (s, yol.name, str(yol.parent), "" if klasor else yol.suffix.lower().lstrip("."),
                        boyut, degisme, tur, durum, ders, dosya_normalle(yol.name), dosya_normalle(s)))
            sonuc.degisen += 1
        elif (eski["boyut"], eski["degisme"], eski["tur"]) != (boyut, degisme, tur):
            db.execute("DELETE FROM sayfa WHERE dosya_id=?", (eski["id"],))
            db.execute("""UPDATE dosya SET boyut=?,degisme=?,tur=?,durum=?,hata=NULL,sha=NULL,
                          sayfa_sayisi=NULL,ocr_sayfa=NULL,ders=? WHERE id=?""",
                       (boyut, degisme, tur, durum, ders, eski["id"]))
            sonuc.degisen += 1
        elif eski["ders"] != ders:
            db.execute("UPDATE dosya SET ders=? WHERE id=?", (ders, eski["id"]))
            sonuc.degisen += 1
        if sonuc.gorulen % 1000 == 0:
            db.commit()

    def gez(klasor, girdiler):
        # Yalnız açık dizin yineleyicileri yığında; milyon çocuk yolu listelenmez.
        yigin = [(klasor, girdiler)]
        try:
            while yigin:
                ust, it = yigin[-1]
                try:
                    e = next(it)
                except StopIteration:
                    it.close()
                    yigin.pop()
                    continue
                except OSError as hata:
                    koru(ust, hata)
                    if ust == klasor or not _kok_erisim_kontrol(klasor):
                        sonuc.yayin_uygun = False
                    it.close()
                    yigin.pop()
                    continue
                yol = Path(e.path)
                if e.name.casefold() in haric or os.path.normcase(os.path.abspath(yol)) in haric_yollar:
                    # Hariç tutulan klasörler taranmaz; korunan tablosuna eklenmez
                    continue
                try:
                    st = e.stat(follow_symlinks=False)
                    klasor_mu = e.is_dir(follow_symlinks=False)
                    satir(yol, st, klasor_mu)
                    if klasor_mu and not e.is_symlink() and not getattr(e, "is_junction", lambda: False)():
                        yigin.append((yol, os.scandir(yol)))
                except OSError as hata:
                    koru(yol, hata)
                    if not _kok_erisim_kontrol(klasor):
                        sonuc.yayin_uygun = False
        finally:
            for _, it in yigin:
                it.close()

    acilan_kokler = []
    for ham in kokler:
        kok = Path(os.path.abspath(ham))
        try:
            girdiler = os.scandir(kok)
        except OSError as e:
            log.warning("Kök açılamadı; bu tur yayın yok: %s (%s)", kok, e)
            sonuc.yayin_uygun = False
            koru(kok, e)
            continue
        gez(kok, girdiler)
        acilan_kokler.append(kok)
        # Kök klasörün kendi satırı (üst klasör daha önce tarandıysa) silinmesin
        db.execute("INSERT OR IGNORE INTO gorulen(yol) VALUES (?)", (_kanon(kok),))
    # Bütün kökler bittikten sonra sil; görülen yolların tamamı diskte kalır.
    for kok in acilan_kokler:
        k_kok = _kanon(kok)
        desen = _alt_deseni(k_kok)
        toplam = db.execute("SELECT count(*) FROM dosya WHERE kanon(yol)=? OR kanon(yol) LIKE ? ESCAPE '!'",
                            (k_kok, desen)).fetchone()[0]
        sorgu = """SELECT id FROM dosya d WHERE (kanon(d.yol)=? OR kanon(d.yol) LIKE ? ESCAPE '!')
                    AND NOT EXISTS (SELECT 1 FROM gorulen g WHERE g.yol=kanon(d.yol))
                    AND NOT EXISTS (SELECT 1 FROM korunan k WHERE kanon(d.yol)=k.yol OR kanon(d.yol) LIKE k.desen ESCAPE '!')"""
        silinecek = db.execute("SELECT count(*) FROM (" + sorgu + ")", (k_kok, desen)).fetchone()[0]
        if silinecek > toplam * .5 and not zorla_sil:
            log.warning("Silme koruması: %s, %d/%d kayıt; --zorla-sil ile aşılabilir", kok, silinecek, toplam)
        else:
            while True:
                ids = db.execute(sorgu + " LIMIT ?", (k_kok, desen, 1000)).fetchall()
                if not ids:
                    break
                db.executemany("DELETE FROM dosya WHERE id=?", [(r[0],) for r in ids])
                db.commit()
                sonuc.silinen += len(ids)
        db.commit()
    db.commit()
    return sonuc
