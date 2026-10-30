"""Sayfa başına atomik disk önbelleği; hiçbir görüntü bellekte biriktirilmez."""
import json
import os
from dataclasses import asdict
from pathlib import Path
import unicodedata

from .model import OcrSatir, Sayfa


def json_yaz(yol: Path, veri):
    yol.parent.mkdir(parents=True, exist_ok=True)
    gecici = yol.with_name(yol.name + ".tmp")
    with gecici.open("w", encoding="utf-8", newline="\n") as f:
        json.dump(veri, f, ensure_ascii=False)
        f.flush()
        os.fsync(f.fileno())
    os.replace(gecici, yol)


def sayfa_yaz(ara: Path, sayfa: Sayfa, dpi: int, uyarilar=()):
    sayfa.metin = unicodedata.normalize("NFKC", sayfa.metin).strip()
    for satir in sayfa.satirlar:
        satir.metin = unicodedata.normalize("NFKC", satir.metin).strip()
    veri = asdict(sayfa)
    veri.update(dpi=dpi, uyarilar=list(uyarilar))
    json_yaz(ara / f"sayfa_{sayfa.no:05d}.json", veri)


def sayfa_oku(ara: Path, no: int, dpi: int):
    try:
        veri = json.loads((ara / f"sayfa_{no:05d}.json").read_text(encoding="utf-8"))
        if veri.pop("dpi") != dpi:
            return None
        uyarilar = veri.pop("uyarilar", [])
        veri["satirlar"] = [OcrSatir(s["metin"], s["guven"], tuple(s["kutu"]))
                            for s in veri["satirlar"]]
        if veri["gorsel_boyut"] is not None:
            veri["gorsel_boyut"] = tuple(veri["gorsel_boyut"])
        return Sayfa(**veri), uyarilar
    except (OSError, ValueError, KeyError, TypeError):
        return None
