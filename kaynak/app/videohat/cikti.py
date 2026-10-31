"""VideoHat altyazı (SRT, VTT), düz metin (TXT) ve JSON meta veri çıktı modülü."""
import hashlib
import json
import textwrap
from pathlib import Path

from .cihaz import CihazSecimi
from .stt import Segment


def _ms_hesapla(sn: float) -> tuple[int, int, int, int]:
    toplam_ms = max(0, round(sn * 1000))
    ms = toplam_ms % 1000
    toplam_sn = toplam_ms // 1000
    saniye = toplam_sn % 60
    toplam_dakika = toplam_sn // 60
    dakika = toplam_dakika % 60
    saat = toplam_dakika // 60
    return saat, dakika, saniye, ms


def _srt_zaman(sn: float) -> str:
    saat, dakika, saniye, ms = _ms_hesapla(sn)
    return f"{saat:02d}:{dakika:02d}:{saniye:02d},{ms:03d}"


def _vtt_zaman(sn: float) -> str:
    saat, dakika, saniye, ms = _ms_hesapla(sn)
    return f"{saat:02d}:{dakika:02d}:{saniye:02d}.{ms:03d}"


def _segmentleri_cuelara_bol(seg: list[Segment]) -> list[tuple[float, float, str]]:
    """Segment metinlerini satır başına en çok 42 karakter ve kart başına en çok 2 satır olacak şekilde zaman oranlı alt cue'lara böler."""
    cuelar: list[tuple[float, float, str]] = []

    for s in seg:
        metin = " ".join(s.metin.strip().split())
        if not metin:
            continue

        satirlar = textwrap.wrap(metin, width=42, break_long_words=True)
        if not satirlar:
            continue

        # En çok 2 satırlık gruplara ayır
        parcalar = [satirlar[i : i + 2] for i in range(0, len(satirlar), 2)]
        cue_metinleri = ["\n".join(p) for p in parcalar]

        if len(cue_metinleri) == 1:
            cuelar.append((s.bas, s.son, cue_metinleri[0]))
            continue

        harf_sayilari = [len(m) for m in cue_metinleri]
        toplam_harf = sum(harf_sayilari)
        toplam_sure = max(0.0, s.son - s.bas)

        su_anki_bas = s.bas
        for i, (c_metin, harf) in enumerate(zip(cue_metinleri, harf_sayilari)):
            if i == len(cue_metinleri) - 1:
                su_anki_son = s.son
            else:
                oran = (harf / toplam_harf) if toplam_harf > 0 else (1.0 / len(cue_metinleri))
                su_anki_son = su_anki_bas + (toplam_sure * oran)
            cuelar.append((su_anki_bas, su_anki_son, c_metin))
            su_anki_bas = su_anki_son

    return cuelar


def srt(seg: list[Segment], yol: Path) -> None:
    """Segmentleri UTF-8 (BOM'suz) SRT altyazı dosyası olarak kaydeder."""
    yol = Path(yol)
    yol.parent.mkdir(parents=True, exist_ok=True)
    cuelar = _segmentleri_cuelara_bol(seg)
    bloklar = []
    for sira, (bas, son, metin) in enumerate(cuelar, start=1):
        bas_str = _srt_zaman(bas)
        son_str = _srt_zaman(son)
        bloklar.append(f"{sira}\n{bas_str} --> {son_str}\n{metin}")
    icerik = "\n\n".join(bloklar) + ("\n" if bloklar else "")
    yol.write_text(icerik, encoding="utf-8")


def vtt(seg: list[Segment], yol: Path) -> None:
    """Segmentleri UTF-8 (BOM'suz) WebVTT altyazı dosyası olarak kaydeder."""
    yol = Path(yol)
    yol.parent.mkdir(parents=True, exist_ok=True)
    cuelar = _segmentleri_cuelara_bol(seg)
    bloklar = []
    for bas, son, metin in cuelar:
        bas_str = _vtt_zaman(bas)
        son_str = _vtt_zaman(son)
        bloklar.append(f"{bas_str} --> {son_str}\n{metin}")
    if bloklar:
        icerik = "WEBVTT\n\n" + "\n\n".join(bloklar) + "\n"
    else:
        icerik = "WEBVTT\n\n"
    yol.write_text(icerik, encoding="utf-8")


def txt(seg: list[Segment], yol: Path) -> None:
    """Segmentleri satır satır UTF-8 (BOM'suz) düz metin dosyası olarak kaydeder."""
    yol = Path(yol)
    yol.parent.mkdir(parents=True, exist_ok=True)
    satirlar = [s.metin.strip() for s in seg if s.metin and s.metin.strip()]
    icerik = "\n".join(satirlar) + ("\n" if satirlar else "")
    yol.write_text(icerik, encoding="utf-8")


def json_meta(
    seg: list[Segment],
    secim: CihazSecimi,
    sureler: dict[str, float],
    yol: Path,
    dil: str | None = None,
    ceviriler: list[str] | None = None,
    ceviri_model: str | None = None,
) -> None:
    """Transkripsiyon ve yürütme üstverilerini UTF-8 JSON dosyası olarak kaydeder."""
    yol = Path(yol)
    yol.parent.mkdir(parents=True, exist_ok=True)
    segmentler = [
        {
            "no": i,
            "bas": round(s.bas, 3),
            "son": round(s.son, 3),
            "metin": s.metin.strip(),
        }
        for i, s in enumerate(seg)
    ]
    segmentler_json = json.dumps(segmentler, ensure_ascii=False, separators=(",", ":"))
    kaynak_ozet = hashlib.sha256(segmentler_json.encode("utf-8")).hexdigest()

    if ceviriler is not None:
        for i, c in enumerate(ceviriler):
            if i < len(segmentler) and c and c.strip():
                segmentler[i]["ceviri"] = c.strip()

    veri = {
        "sema_surumu": 2,
        "cihaz": secim.device,
        "compute_type": secim.compute_type,
        "model": secim.model_ad,
        "neden": secim.neden,
        "sureler": sureler,
        "segment_sayisi": len(seg),
        "sure_sn": round(max((s.son for s in seg), default=0.0), 2),
        "kaynak_ozet": kaynak_ozet,
        "segmentler": segmentler,
    }
    if dil:
        veri["dil"] = dil
    if ceviriler is not None:
        veri["ceviri_dil"] = "tr"
        if ceviri_model:
            veri["ceviri_model"] = ceviri_model
    elif ceviri_model:
        veri["ceviri_model"] = ceviri_model

    icerik = json.dumps(veri, ensure_ascii=False, indent=2) + "\n"
    yol.write_text(icerik, encoding="utf-8")
