"""Komut satırı: python -m videohat {isle,izle,kontrol}. Çıkış: 0 ok, 1 iş hatası, 2 ortam hatası."""
import argparse
import logging
import os
import sys
from pathlib import Path

from . import yollar
from .hatalar import AyarHatasi, OrtamHatasi

VIDEO_UZANTILARI = {".mp4", ".mkv", ".mov", ".avi", ".m4a", ".wav", ".mp3"}
CIKTI_KLASOR_EKLERI = ("_islenmis", "_videoperisi")


def _cikti_klasoru_mu(ad: str) -> bool:
    """Yeni ve eski çıktı klasörlerinin girdiler gibi taranmasını önler."""
    kucuk = ad.lower()
    return any(kucuk.endswith(ek) for ek in CIKTI_KLASOR_EKLERI)


def _gunluk_kur(kok: Path) -> None:
    from datetime import date
    d = kok / "is" / "gunluk"
    d.mkdir(parents=True, exist_ok=True)
    bicim = logging.Formatter("%(asctime)s %(levelname)s %(message)s")
    log = logging.getLogger("videohat")
    log.setLevel(logging.INFO)
    if log.handlers:
        return
    for h in (logging.StreamHandler(sys.stdout),
              logging.FileHandler(d / f"{date.today():%Y-%m-%d}.log", encoding="utf-8")):
        h.setFormatter(bicim)
        log.addHandler(h)


def _topla(girdi: Path) -> list[Path]:
    if girdi.is_dir():
        if _cikti_klasoru_mu(girdi.name):
            return []
        return sorted(p for p in girdi.rglob("*")
                      if p.is_file() and p.suffix.lower() in VIDEO_UZANTILARI
                      and not p.stem.lower().endswith("_islenmis")
                      and not any(_cikti_klasoru_mu(a) or a == "_ara"
                                  for a in p.relative_to(girdi).parent.parts))
    return [girdi]


def _cikis_belirle(
    video: Path,
    girdi_koku: Path,
    cikis_arg: str | Path | None,
    tek_dosya_mi: bool = False,
    kullanilan: set[Path] | None = None,
) -> Path:
    """
    Çıktı klasörünü belirler.
    Tek dosya girdisinde --cikis aynen korunur.
    Klasör girdisinde <cikis>/<bagil>/<stem>_islenmis düzeni ve çakışma koruması uygulanır.
    """
    if tek_dosya_mi:
        return Path(cikis_arg) if cikis_arg else video.parent / f"{video.stem}_islenmis"

    if cikis_arg:
        try:
            bagil = video.relative_to(girdi_koku).parent
        except ValueError:
            bagil = Path()
        hedef = Path(cikis_arg) / bagil / f"{video.stem}_islenmis"
    else:
        hedef = video.parent / f"{video.stem}_islenmis"

    def _cakisiyor(p: Path) -> bool:
        if kullanilan is not None and p in kullanilan:
            return True
        try:
            if p.exists():
                return True
        except OSError:
            pass
        return False

    if _cakisiyor(hedef):
        aday = hedef.parent / f"{video.stem}_{video.suffix.lstrip('.')}_islenmis"
        sayac = 1
        while _cakisiyor(aday):
            aday = (
                hedef.parent
                / f"{video.stem}_{video.suffix.lstrip('.')}_{sayac}_islenmis"
            )
            sayac += 1
        hedef = aday

    if kullanilan is not None:
        kullanilan.add(hedef)

    return hedef


def _isle(args) -> int:
    from . import ayar as ayar_mod
    from .hat import Bayraklar, isle_tek
    kok = yollar.kok()
    ayar = ayar_mod.yukle(kok)
    log = logging.getLogger("videohat")
    bay = Bayraklar(agir_ses=args.agir_ses, goruntu=args.goruntu, upscale=args.upscale,
                    model=args.model, cihaz=args.cihaz)
    hatali = 0
    tek_dosya_girdisi = len(args.girdi) == 1 and Path(args.girdi[0]).is_file()
    kullanilan_ciktilar: set[Path] = set()

    for yol in args.girdi:
        p = Path(yol)
        if not p.exists():
            log.error("Bulunamadı: %s", p)
            hatali += 1
            continue
        for video in _topla(p):
            cikis = _cikis_belirle(
                video,
                girdi_koku=p,
                cikis_arg=args.cikis,
                tek_dosya_mi=tek_dosya_girdisi,
                kullanilan=kullanilan_ciktilar,
            )
            cikis.mkdir(parents=True, exist_ok=True)
            log.info("İşleniyor: %s", video)
            s = isle_tek(video, cikis, ayar, bay)
            if s.basarili:
                log.info("Bitti: %s (%s) süreler=%s", video.name, s.cihaz,
                         {k: round(v, 1) for k, v in s.sureler.items()})
            else:
                log.error("HATA: %s: %s", video.name, s.hata)
                hatali += 1
    return 1 if hatali else 0


def _isle_grup(args) -> int:
    from . import ayar as ayar_mod, grup
    from .hat import Bayraklar
    kok = yollar.kok()
    ayar = ayar_mod.yukle(kok)
    log = logging.getLogger("videohat")
    bay = Bayraklar(agir_ses=args.agir_ses, goruntu=args.goruntu,
                    model=args.model, cihaz=args.cihaz)
    klasor = Path(args.klasor)
    if not klasor.exists() or not klasor.is_dir():
        log.error("Klasör bulunamadı: %s", klasor)
        return 1
    sonuc = grup.isle_grup(
        klasor,
        ayar,
        bay,
        referans_kanal_adi=args.referans,
        ses_enerji_fn=grup.varsayilan_ses_enerjisi,
    )
    for u in sonuc.uyarilar:
        log.warning("Grup uyarısı: %s", u)
    if not sonuc.basarili:
        log.error("Grup işleme başarısız: %s", sonuc.hata)
        return 1
    log.info("Grup işleme bitti: %s (%s)", sonuc.ders_adi, sonuc.cikis_klasoru)
    return 0


def _belge(args) -> int:
    from . import ayar as ayar_mod
    from .belge import UZANTILAR
    from .belge.hat import disa_yaz
    from .belge.ocr import OcrHavuzu
    ayar = ayar_mod.yukle(yollar.kok())
    if args.profil:
        ayar.ocr.profil = args.profil
    log = logging.getLogger("videohat")
    hatali = 0
    tek = len(args.girdi) == 1 and Path(args.girdi[0]).is_file()
    kullanilan = set()

    def belgeler(p):
        if not p.is_dir():
            yield p
            return
        for kok, dizinler, dosyalar in os.walk(p):
            dizinler[:] = [d for d in dizinler if not d.lower().endswith("_belge")
                          and d.casefold() not in {h.casefold() for h in ayar.dizin.haric}]
            for ad in sorted(dosyalar):
                if Path(ad).suffix.lower() in UZANTILAR:
                    yield Path(kok) / ad

    with OcrHavuzu(ayar) as havuz:
        for ham in args.girdi:
            girdi = Path(ham)
            for p in belgeler(girdi):
                if tek and args.cikis:
                    cikis = Path(args.cikis)
                else:
                    taban = Path(args.cikis) if args.cikis else p.parent
                    if args.cikis and girdi.is_dir():
                        taban /= p.relative_to(girdi).parent
                    cikis = taban / f"{p.stem}_belge"
                    if cikis in kullanilan:
                        cikis = taban / f"{p.stem}_{p.suffix.lstrip('.')}_belge"
                kullanilan.add(cikis)
                try:
                    kayit = disa_yaz(p, cikis, ayar, havuz)
                    if not kayit.basarili:
                        log.error("%s: %s", p, kayit.hata)
                        hatali += 1
                except OrtamHatasi:
                    raise
                except Exception as e:
                    log.error("Belge işlenemedi: %s: %s", p, e)
                    hatali += 1
    return 1 if hatali else 0


def _dizinle(args) -> int:
    import sqlite3
    from . import ayar as ayar_mod
    from .dizin.dizinle import calistir
    from .hatalar import IsHatasi
    ayar = ayar_mod.yukle(yollar.kok())
    if args.profil:
        ayar.ocr.profil = args.profil
    try:
        return calistir(ayar, args.kok, args.depo, args.bir_kez, args.yalniz_ad, args.zorla_sil)
    except (IsHatasi, OSError, sqlite3.Error) as e:
        logging.getLogger("videohat").error("Dizinleme hatası: %s", e)
        return 1


def parser_olustur() -> argparse.ArgumentParser:
    ap = argparse.ArgumentParser(prog="videohat")
    alt = ap.add_subparsers(dest="komut", required=True)
    i = alt.add_parser("isle", help="dosya veya klasör işle")
    i.add_argument("girdi", nargs="+")
    i.add_argument("--cikis", help="çıktı klasörü (varsayılan: videonun yanında <ad>_islenmis)")
    i.add_argument("--agir-ses", action="store_true", help="DeepFilterNet ağır mod")
    i.add_argument("--goruntu", action="store_true", help="görüntü iyileştirme")
    i.add_argument("--upscale", action="store_true", help="Real-ESRGAN x2 (isteğe bağlı)")
    i.add_argument("--model", choices=["large-v3", "medium", "turbo"])
    i.add_argument("--cihaz", choices=["cpu", "cuda"])
    ig = alt.add_parser("isle-grup", help="çok kameralı ders klasörünü işle")
    ig.add_argument("klasor", help="ders klasörü")
    ig.add_argument("--referans", help="referans kamera/kanal adı")
    ig.add_argument("--goruntu", action="store_true", help="görüntü iyileştirme")
    ig.add_argument("--agir-ses", action="store_true", help="DeepFilterNet ağır mod")
    ig.add_argument("--model", choices=["large-v3", "medium", "turbo"])
    ig.add_argument("--cihaz", choices=["cpu", "cuda"])
    alt.add_parser("izle", help="is\\gelen klasörünü izle")
    alt.add_parser("kontrol", help="kendi kendine test")
    d = alt.add_parser("dizinle", help="dosya ve belge dizinini güncelle")
    d.add_argument("--kok", action="append", help="taranacak kök; tekrarlanabilir")
    d.add_argument("--depo", help="dizin deposu")
    d.add_argument("--bir-kez", action="store_true")
    d.add_argument("--yalniz-ad", action="store_true")
    d.add_argument("--zorla-sil", action="store_true", help="yüzde 50 silme korumasını aş")
    d.add_argument("--profil", choices=["hizli", "uyarlamali", "kalite"])
    b = alt.add_parser("belge", help="belgeden metin ve aranabilir PDF üret")
    b.add_argument("girdi", nargs="+")
    b.add_argument("--cikis")
    b.add_argument("--profil", choices=["hizli", "uyarlamali", "kalite"])
    return ap


def main(argv=None) -> int:
    ap = parser_olustur()
    args = ap.parse_args(argv)
    try:
        kok = yollar.kok()
        _gunluk_kur(kok)
        if os.environ.get("VIDEOHAT_AG_KILIDI") == "1":
            from . import ag_kilidi
            ag_kilidi.kur()
        if args.komut == "isle":
            return _isle(args)
        if args.komut == "isle-grup":
            return _isle_grup(args)
        if args.komut == "belge":
            return _belge(args)
        if args.komut == "dizinle":
            return _dizinle(args)
        if args.komut == "izle":
            from . import ayar as ayar_mod, izle
            return izle.dongu(ayar_mod.yukle(kok))
        from . import kontrol
        return kontrol.calistir()
    except (OrtamHatasi, AyarHatasi) as e:
        logging.getLogger("videohat").error("Ortam/ayar hatası: %s", e)
        return 2
    except KeyboardInterrupt:
        return 0


if __name__ == "__main__":
    sys.exit(main())
