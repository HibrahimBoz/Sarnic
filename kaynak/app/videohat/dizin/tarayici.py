"""Akış halinde katalog taraması; erişilemeyen dallarda silme koruması."""
from dataclasses import dataclass
import logging
import os
from pathlib import Path

from ..belge import BELGE_UZANTILARI, GORUNTU_UZANTILARI
from .normal import dosya_normalle

log = logging.getLogger("videohat")
VIDEO = frozenset(".mp4 .mkv .mov .avi .m4a .wav .mp3".split())


@dataclass
class TaramaSonucu:
    gorulen: int = 0
    degisen: int = 0
    silinen: int = 0
    yayin_uygun: bool = True


def tur_bul(yol, klasor=False):
    if klasor:
        return "klasor"
    uz = yol.suffix.lower()
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
        db.execute("INSERT OR IGNORE INTO korunan(yol,desen) VALUES (?,?)", (str(yol), _alt_deseni(yol)))

    def satir(yol, st, klasor):
        s = str(yol)
        db.execute("INSERT OR IGNORE INTO gorulen(yol) VALUES (?)", (s,))
        sonuc.gorulen += 1
        tur = tur_bul(yol, klasor)
        boyut = 0 if klasor else st.st_size
        degisme = int(st.st_mtime)
        ders = ders_bul(yol)
        eski = db.execute("SELECT id,boyut,degisme,tur,ders FROM dosya WHERE yol=?", (s,)).fetchone()
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
                    if ust == klasor:
                        sonuc.yayin_uygun = False
                    it.close()
                    yigin.pop()
                    continue
                yol = Path(e.path)
                if e.name.casefold() in haric or os.path.normcase(os.path.abspath(yol)) in haric_yollar:
                    # Ayar değişince hariç tutulan eski kayıtlar da korunur.
                    db.execute("INSERT OR IGNORE INTO korunan(yol,desen) VALUES (?,?)", (str(yol), _alt_deseni(yol)))
                    continue
                try:
                    st = e.stat(follow_symlinks=False)
                    klasor_mu = e.is_dir(follow_symlinks=False)
                    satir(yol, st, klasor_mu)
                    if klasor_mu and not e.is_symlink() and not getattr(e, "is_junction", lambda: False)():
                        yigin.append((yol, os.scandir(yol)))
                except OSError as hata:
                    koru(yol, hata)
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
    # Bütün kökler bittikten sonra sil; görülen yolların tamamı diskte kalır.
    for kok in acilan_kokler:
        desen = _alt_deseni(kok)
        toplam = db.execute("SELECT count(*) FROM dosya WHERE yol LIKE ? ESCAPE '!'", (desen,)).fetchone()[0]
        sorgu = """SELECT id FROM dosya d WHERE d.yol LIKE ? ESCAPE '!'
                    AND NOT EXISTS (SELECT 1 FROM gorulen g WHERE g.yol=d.yol)
                    AND NOT EXISTS (SELECT 1 FROM korunan k WHERE d.yol=k.yol OR d.yol LIKE k.desen ESCAPE '!')"""
        silinecek = db.execute("SELECT count(*) FROM (" + sorgu + ")", (desen,)).fetchone()[0]
        if silinecek > toplam * .5 and not zorla_sil:
            log.warning("Silme koruması: %s, %d/%d kayıt; --zorla-sil ile aşılabilir", kok, silinecek, toplam)
        else:
            while True:
                ids = db.execute(sorgu + " LIMIT ?", (desen, 1000)).fetchall()
                if not ids:
                    break
                db.executemany("DELETE FROM dosya WHERE id=?", [(r[0],) for r in ids])
                db.commit()
                sonuc.silinen += len(ids)
        db.commit()
    db.commit()
    return sonuc
