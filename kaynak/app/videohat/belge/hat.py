"""SHA256 ile tekilleşen, sayfa önbelleğinden devam edebilen belge hattı."""
from contextlib import nullcontext
import hashlib
import json
import logging
import os
from pathlib import Path
import shutil
import time

from .. import yollar
from ..hatalar import IsHatasi
from .cikar import cikar
from .model import BelgeKaydi
from .ocr import OcrHavuzu
from .onbellek import json_yaz
from .pdf_katman import olustur

SURUM = 1
log = logging.getLogger("videohat")


def sha256(yol):
    h = hashlib.sha256()
    with Path(yol).open("rb") as f:
        for parca in iter(lambda: f.read(1024 * 1024), b""):
            h.update(parca)
    return h.hexdigest()


def isle_belge(yol: Path, depo: Path, ayar, havuz: OcrHavuzu | None = None) -> BelgeKaydi:
    bas = time.monotonic()
    yol, depo = Path(yol), Path(depo)
    sha = sha256(yol)
    hedef = depo / "belgeler" / sha[:2] / sha
    belge_json = hedef / "belge.json"
    if belge_json.is_file():
        try:
            veri = json.loads(belge_json.read_text(encoding="utf-8"))
            if (veri.get("surum") == SURUM
                    and veri.get("durum") == "tamam"
                    and veri.get("bicim") == yol.suffix.lower().lstrip(".")):
                return BelgeKaydi(sha, hedef, veri)
        except (OSError, ValueError):
            pass
    ara = hedef / "_ara"
    ara.mkdir(parents=True, exist_ok=True)
    with (OcrHavuzu(ayar) if havuz is None else nullcontext(havuz)) as motor:
        sonuc = cikar(yol, ayar, motor, ara)
    # Çıkarma sırasında değişen dosyayı eski SHA altında yayımlama.
    if sha256(yol) != sha:
        raise IsHatasi("Belge işlenirken değişti; yeniden taranmalı")
    if sonuc.durum == "tamam":
        sonuc.aranabilir_pdf = olustur(yol, sonuc.sayfalar, hedef / "aranabilir.pdf", ayar.ocr.dpi)
        metin_gecici = hedef / "metin.txt.tmp"
        with metin_gecici.open("w", encoding="utf-8", newline="\n") as f:
            for i, s in enumerate(sonuc.sayfalar):
                if i:
                    f.write("\f")
                f.write(s.metin)
            f.flush()
            os.fsync(f.fileno())
        os.replace(metin_gecici, hedef / "metin.txt")
    veri = {"surum": SURUM, "sha": sha, "kaynak_ad": yol.name, "bicim": sonuc.bicim,
            "sayfa_sayisi": len(sonuc.sayfalar), "ocr_sayfa": sum(s.kaynak == "ocr" for s in sonuc.sayfalar),
            "aranabilir_pdf": "aranabilir.pdf" if sonuc.aranabilir_pdf else None,
            "sure_sn": round(time.monotonic() - bas, 3),
            "motor": {"ocr": "PP-OCRv6 " + ayar.ocr.profil,
                      "tesseract": str(yollar.tesseract()) if ayar.ocr.tesseract and yollar.tesseract() else None},
            "sayfalar": [{k: getattr(s, k) for k in ("no", "etiket", "kaynak", "guven", "metin")}
                         for s in sonuc.sayfalar], "uyarilar": sonuc.uyarilar,
            "durum": sonuc.durum, "hata": sonuc.hata}
    if sonuc.durum == "tamam":
        json_yaz(belge_json, veri)  # Tamam işareti en son.
    if not ayar.genel.ara_dosya_sakla:
        shutil.rmtree(ara, ignore_errors=True)
    log.info("Belge: %s, %d sayfa, %d OCR", yol.name, veri["sayfa_sayisi"], veri["ocr_sayfa"])
    return BelgeKaydi(sha, hedef, veri)


def disa_yaz(yol, cikis, ayar, havuz=None):
    """Bağımsız komut/izleyici için aynı hattı düz çıktı klasörüne aktarır."""
    cikis = Path(cikis)
    cikis.mkdir(parents=True, exist_ok=True)
    depo = cikis / "_ara" / "depo"
    try:
        kayit = isle_belge(yol, depo, ayar, havuz)
        for ad in ("metin.txt", "aranabilir.pdf", "belge.json"):
            kaynak = kayit.hedef / ad
            if kaynak.is_file():
                gecici = cikis / (ad + ".tmp")
                shutil.copyfile(kaynak, gecici)
                os.replace(gecici, cikis / ad)
            elif ad == "aranabilir.pdf":
                (cikis / ad).unlink(missing_ok=True)
        kayit.hedef = cikis
        return kayit
    finally:
        if not ayar.genel.ara_dosya_sakla:
            shutil.rmtree(cikis / "_ara", ignore_errors=True)
