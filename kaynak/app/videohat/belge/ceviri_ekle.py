"""Belge sayfalarına dil tahmini ve Türkçe çeviri ekleme modülü."""
import logging
from typing import Any

from ..ceviri import dil_tahmin

log = logging.getLogger("videohat")


def ceviri_ekle(veri: dict[str, Any], cevirmen: Any = None) -> bool:
    """Her sayfaya dil yazar; dil != 'tr' ise çeviri ekler; köke 'ceviri_surum': 1.

    Değişiklik olduysa True döndürür.
    cevirmen is None ise yalnız dil yazılır, ceviri_surum yazılmaz.
    """
    if cevirmen is None:  # dil de yazılmaz: dizin çeviri geçişi "dil IS NULL" ile yeniden dener
        return False
    degisti = False
    sayfalar = veri.get("sayfalar", [])

    for sayfa in sayfalar:
        metin = sayfa.get("metin", "")
        mevcut_dil = sayfa.get("dil")
        if not mevcut_dil:
            dil = dil_tahmin(metin) if metin else "tr"
            sayfa["dil"] = dil
            degisti = True
        else:
            dil = mevcut_dil

        if cevirmen is not None:
            if dil != "tr" and metin and metin.strip():
                if "ceviri" not in sayfa or not sayfa["ceviri"]:
                    try:
                        sonuclar = cevirmen.cevir([metin], kaynak_dil=dil)
                        if sonuclar and sonuclar[0] and sonuclar[0].strip():
                            sayfa["ceviri"] = sonuclar[0].strip()
                            degisti = True
                    except Exception as e:
                        log.warning("Çeviri yapılamadı: %s", e)
                        if "uyarilar" in veri and isinstance(veri["uyarilar"], list):
                            veri["uyarilar"].append(f"Çeviri yapılamadı: {e}")

    if cevirmen is not None:
        if veri.get("ceviri_surum") != 1:
            veri["ceviri_surum"] = 1
            degisti = True

    return degisti
