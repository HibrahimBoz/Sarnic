"""Belge sayfalarına dil tahmini ve Türkçe çeviri ekleme modülü."""
import json
import logging
from typing import Any

from ..ceviri import dil_tahmin

log = logging.getLogger("videohat")


def ceviri_ekle(veri: dict[str, Any], cevirmen: Any = None) -> bool:
    """Her sayfaya dil yazar; dil != 'tr' ise çeviri ekler; köke 'ceviri_surum': 1.

    Değişiklik olduysa True döndürür.
    cevirmen None ise hiçbir şey yazılmaz: dizin çeviri geçişi "dil IS NULL" ile model gelince yeniden dener.
    Çeviri hata verirse o dilin sayfalarına dil yazılmaz ve ceviri_surum eklenmez; sonraki geçiş yeniden dener.
    """
    if cevirmen is None:
        return False
    # Hata sonrası geri alınan dil "değişiklik" sayılmasın: sonuç önceki durumla karşılaştırılır.
    once = json.dumps([veri.get("sayfalar"), veri.get("ceviri_surum")], ensure_ascii=False, sort_keys=True)
    izinli = getattr(cevirmen, "diller", None)  # ayar.ceviri.diller; sahte çevirmende yok
    bekleyen: dict[str, list[dict[str, Any]]] = {}

    for sayfa in veri.get("sayfalar", []):
        metin = sayfa.get("metin", "")
        dil = sayfa.get("dil")
        if not dil:
            dil = dil_tahmin(metin) if metin else "tr"
            sayfa["dil"] = dil
        if (dil != "tr" and metin and metin.strip() and not sayfa.get("ceviri")
                and (izinli is None or dil in izinli)):
            bekleyen.setdefault(dil, []).append(sayfa)

    hata_var = False
    # Dil başına tek toplu çağrı: sayfa başına ayrı çağrı GPU'yu boş bekletir.
    for dil, sayfalar in bekleyen.items():
        try:
            sonuclar = cevirmen.cevir([s["metin"] for s in sayfalar], kaynak_dil=dil)
        except Exception as e:
            hata_var = True
            log.warning("Çeviri yapılamadı: %s", e)
            uyari = f"Çeviri yapılamadı: {e}"
            if isinstance(veri.get("uyarilar"), list) and uyari not in veri["uyarilar"]:
                veri["uyarilar"].append(uyari)
            for s in sayfalar:
                s.pop("dil", None)
            continue
        for s, c in zip(sayfalar, sonuclar):
            if c and c.strip():
                s["ceviri"] = c.strip()

    if not hata_var:
        veri["ceviri_surum"] = 1
    return json.dumps([veri.get("sayfalar"), veri.get("ceviri_surum")], ensure_ascii=False, sort_keys=True) != once
