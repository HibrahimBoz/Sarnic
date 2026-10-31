"""Katalog önce yayımlanır, ardından bekleyen belgeler küçükten büyüğe işlenir."""
import json
import logging
import sqlite3
from pathlib import Path
import time

from ..belge.ceviri_ekle import ceviri_ekle
from ..belge.hat import isle_belge
from ..belge.ocr import OcrHavuzu
from ..belge.onbellek import json_yaz
from ..ceviri import cevirmen_olustur
from ..hatalar import AyarHatasi, IsHatasi, OrtamHatasi
from . import vt
from .tarayici import tara
from .yayin import Yayinci

log = logging.getLogger("videohat")


def icerikleri_isle(db, depo, ayar, havuz, yayinci=None, cevirmen=None):
    hatali, son_yayin = 0, time.monotonic()
    try:  # tablo var ama boşsa her sorguya pahalı NOT EXISTS eklenmesin
        koruma_var = db.execute("SELECT 1 FROM korunan LIMIT 1").fetchone()
    except sqlite3.OperationalError:
        koruma_var = None
    koruma = (" AND NOT EXISTS (SELECT 1 FROM korunan k WHERE kanon(dosya.yol)=k.yol "
              "OR kanon(dosya.yol) LIKE k.desen ESCAPE '!')") if koruma_var else ""

    def ilerleme():
        nonlocal son_yayin
        if yayinci and time.monotonic() - son_yayin >= ayar.dizin.yayin_dk * 60:
            try:
                yayinci.yayinla()
            except (OSError, sqlite3.Error, IsHatasi) as e:
                log.warning("Ara yayın yapılamadı; yerel işleme sürüyor: %s", e)
            son_yayin = time.monotonic()

    havuz.ilerleme = ilerleme
    son_boyut, son_id = -1, 0
    while True:
        # Boyut sırasındaki son anahtardan devam et; kuyruğu tekrar tekrar sıralama.
        kayitlar = db.execute("SELECT id,yol,boyut FROM dosya INDEXED BY ix_dosya_boyut "
                             "WHERE (boyut,id)>(?,?) AND durum=?" + koruma + " ORDER BY boyut,id LIMIT ?",
                             (son_boyut, son_id, "bekliyor", 100)).fetchall()
        if not kayitlar:
            break
        for r in kayitlar:
            son_boyut, son_id = r["boyut"], r["id"]
            if r["boyut"] > ayar.dizin.max_dosya_mb * 1024 * 1024:
                db.execute("UPDATE dosya SET durum=?,hata=? WHERE id=?",
                           ("desteklenmiyor", "Dosya çok büyük", r["id"]))
            else:
                try:
                    kayit = isle_belge(Path(r["yol"]), depo, ayar, havuz)
                    veri = kayit.veri
                    vt.sayfalari_yaz(db, r["id"], veri["sayfalar"])
                    db.execute("UPDATE dosya SET durum=?,hata=?,sha=?,sayfa_sayisi=?,ocr_sayfa=? WHERE id=?",
                               (veri.get("durum", "tamam"), veri.get("hata"), kayit.sha,
                                veri["sayfa_sayisi"], veri["ocr_sayfa"], r["id"]))
                    if veri.get("durum") == "hata":
                        hatali += 1
                except OrtamHatasi:
                    db.rollback()
                    raise
                except Exception as e:
                    db.rollback()
                    hata = f"Belge işlenemedi: {e}"[:500]
                    log.error("%s: %s", r["yol"], hata)
                    db.execute("UPDATE dosya SET durum=?,hata=? WHERE id=?", ("hata", hata, r["id"]))
                    hatali += 1
            db.commit()
            ilerleme()
    havuz.ilerleme = None
    ceviri_gecisi(db, depo, ayar, cevirmen=cevirmen, ilerleme=ilerleme)
    return hatali


def ceviri_gecisi(db, depo, ayar, cevirmen=None, ilerleme=None):
    kayitlar = db.execute(
        "SELECT id, yol, sha FROM dosya "
        "WHERE durum='tamam' AND sha IS NOT NULL AND sha != '' "
        "AND id IN (SELECT dosya_id FROM sayfa WHERE dil IS NULL) "  # kısmi dizin ix_sayfa_dil_yok
        "ORDER BY id"
    ).fetchall()
    if not kayitlar:
        return 0
    if cevirmen is None:
        cevirmen = cevirmen_olustur(ayar)
    if cevirmen is None:  # model yokken dil yazılmaz; model gelince belge yeniden denenir
        return 0
    islenen = 0
    depo = Path(depo)
    for r in kayitlar:
        sha = r["sha"]
        belge_json = depo / "belgeler" / sha[:2] / sha / "belge.json"
        if not belge_json.is_file():
            continue
        try:
            with belge_json.open("r", encoding="utf-8") as f:
                veri = json.load(f)
            degisti = ceviri_ekle(veri, cevirmen)
            if degisti:
                json_yaz(belge_json, veri)
            # Hiçbir sayfaya dil yazılamadıysa (çeviri hatası) dizin satırları yeniden yazılmaz.
            if degisti or any(s.get("dil") for s in veri.get("sayfalar", [])):
                vt.sayfalari_yaz(db, r["id"], veri.get("sayfalar", []))
            db.commit()
            islenen += 1
            if ilerleme:
                ilerleme()
        except OrtamHatasi:
            db.rollback()
            raise
        except Exception as e:
            db.rollback()
            log.warning("Çeviri geçişi hatası (%s): %s", r["yol"], e)
    return islenen


def varsayilan_depo(kok: Path) -> Path:
    for ad in ("_sarnic", "_veridedektifi", "_bulgu"):
        p = kok / ad
        if p.is_dir():
            return p
    return kok / "_sarnic"


def calistir(ayar, kokler=None, depo=None, bir_kez=False, yalniz_ad=False, zorla_sil=False):
    kokler = [Path(p).absolute() for p in (kokler or ayar.dizin.kokler)]
    if not kokler:
        raise AyarHatasi("En az bir --kok veya dizin.kokler gerekli")
    depo = Path(depo or ayar.dizin.depo or varsayilan_depo(kokler[0])).absolute()
    db_yol = Path(ayar.dizin.calisma_db or ayar.kok / "is" / "dizin" / "dizin.db").absolute()
    with vt.yazar_kilidi(db_yol):
        return _dongu(ayar, kokler, depo, db_yol, bir_kez, yalniz_ad, zorla_sil)


def _dongu(ayar, kokler, depo, db_yol, bir_kez, yalniz_ad, zorla_sil):
    db = vt.ac(db_yol)
    hatali = 0
    try:
        with Yayinci(db, depo, ayar.dizin.sakla_yayin) as yayinci, OcrHavuzu(ayar) as havuz:
            while True:
                tarama = tara(db, kokler, ayar.dizin.haric, zorla_sil, [depo, db_yol.parent])
                etkin_yayinci = yayinci if tarama.yayin_uygun else None
                if etkin_yayinci:
                    try:
                        yayinci.yayinla()
                    except (OSError, sqlite3.Error, IsHatasi) as e:
                        log.warning("Tarama sonu yayın yapılamadı: %s", e)
                if not yalniz_ad:
                    hatali += icerikleri_isle(db, depo, ayar, havuz, etkin_yayinci)
                if etkin_yayinci:
                    try:
                        yayinci.yayinla()
                    except (OSError, sqlite3.Error, IsHatasi) as e:
                        log.warning("İçerik sonu yayın yapılamadı: %s", e)
                if bir_kez:
                    return 1 if hatali else 0
                time.sleep(ayar.dizin.tarama_dk * 60)
    except KeyboardInterrupt:
        log.info("Dizinleme durduruldu.")
        return 0
    finally:
        db.close()
