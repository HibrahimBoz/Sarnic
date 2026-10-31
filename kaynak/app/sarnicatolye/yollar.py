"""Paket içi yol çözümleme. Her yol paket köküne göredir; sistem PATH'ine güvenilmez."""
import os
from pathlib import Path
import re

from .hatalar import OrtamHatasi


def kok() -> Path:
    """Paket kökü. SARNICATOLYE_KOK ortam değişkeni varsa o, yoksa app\\sarnicatolye'ın iki üstü."""
    env = os.environ.get("SARNICATOLYE_KOK")
    if env:
        k = env.rstrip("\\/ ") or env
        if len(k) == 2 and k[1] == ":":  # sürücü kökü: "D:" göreli yol olur, "D:\" kalmalı
            k += "\\"
        return Path(k)
    return Path(__file__).resolve().parent.parent.parent


def _gerekli(yol: Path, ne: str) -> Path:
    if not yol.exists():
        raise OrtamHatasi(f"{ne} bulunamadı: {yol}")
    return yol


def ffmpeg() -> Path:
    return _gerekli(kok() / "ffmpeg" / "bin" / "ffmpeg.exe", "ffmpeg.exe")


def ffprobe() -> Path:
    return _gerekli(kok() / "ffmpeg" / "bin" / "ffprobe.exe", "ffprobe.exe")


def model_dizini(ad: str) -> Path:
    d = kok() / "modeller" / ad
    _gerekli(d / "model.bin", f"'{ad}' modeli (model.bin)")
    return d


def model_var_mi(ad: str) -> bool:
    return (kok() / "modeller" / ad / "model.bin").exists()


def deep_filter() -> Path:
    return _gerekli(kok() / "araclar" / "deep-filter.exe", "deep-filter.exe")


def realesrgan() -> Path:
    d = kok() / "araclar" / "realesrgan"
    exe = next(d.rglob("realesrgan-ncnn-vulkan.exe"), None) if d.exists() else None
    if exe is None:
        raise OrtamHatasi(f"realesrgan-ncnn-vulkan.exe bulunamadı: {d}")
    return exe


def genel_model_dizini() -> Path:
    return _gerekli(
        kok() / "araclar" / "realesrgan" / "genel" / "models",
        "realesrgan genel model dizini (models)",
    )


def is_dizini(alt: str) -> Path:
    d = kok() / "is" / alt
    d.mkdir(parents=True, exist_ok=True)
    return d


def ocr_model(ad: str) -> Path:
    return _gerekli(kok() / "modeller-ocr" / ad, f"OCR modeli ({ad})")


def tesseract() -> Path | None:
    p = kok() / "araclar" / "tesseract" / "tesseract.exe"
    return p if p.is_file() else None


def _dosya_surumu(yol: Path) -> tuple[int, ...]:
    if os.name == "nt":
        try:
            import ctypes
            from ctypes import wintypes
            size = ctypes.windll.version.GetFileVersionInfoSizeW(str(yol), None)
            if size > 0:
                res = ctypes.create_string_buffer(size)
                ctypes.windll.version.GetFileVersionInfoW(str(yol), None, size, res)
                r = wintypes.LPVOID()
                l = wintypes.UINT()
                if ctypes.windll.version.VerQueryValueW(res, "\\", ctypes.byref(r), ctypes.byref(l)):
                    import struct
                    info = ctypes.string_at(r, l.value)
                    ms, ls = struct.unpack("II", info[8:16])
                    return (ms >> 16, ms & 0xFFFF, ls >> 16, ls & 0xFFFF)
        except Exception:
            pass
    return ()


def _surum_anahtari(yol: Path) -> tuple[int, ...]:
    ds = _dosya_surumu(yol)
    if ds and any(ds):
        return ds
    surumler = []
    for parca in yol.parts:
        m = re.findall(r"\d+(?:\.\d+)*", parca)
        for s in m:
            if "." in s or parca.lower().startswith("v"):
                try:
                    surumler.append(tuple(int(x) for x in s.split(".")))
                except ValueError:
                    pass
    if surumler:
        return surumler[-1]
    return ()


def _program_dizinleri(adlar=("ProgramFiles", "ProgramFiles(x86)")):
    return [Path(os.environ[ad]) for ad in adlar if os.environ.get(ad)]


def _sistem_program_dizinleri():
    dizinler = []
    for ad in ("ProgramFiles", "ProgramFiles(x86)"):
        v = os.environ.get(ad)
        if v:
            dizinler.append(Path(v))
    lap = os.environ.get("LOCALAPPDATA")
    if lap:
        dizinler.append(Path(lap) / "Programs")
    return dizinler


def libreoffice_adaylari() -> list[Path]:
    adaylar = [kok() / "araclar" / "libreoffice" / "program" / "soffice.exe"]
    adaylar.extend(p / "LibreOffice" / "program" / "soffice.exe" for p in _program_dizinleri())
    bulunanlar = [p for p in adaylar if p.is_file()]
    tek = libreoffice()
    if tek is not None:
        if tek not in bulunanlar:
            return [tek] + bulunanlar
        return bulunanlar
    return []


def libreoffice() -> Path | None:
    adaylar = [kok() / "araclar" / "libreoffice" / "program" / "soffice.exe"]
    adaylar.extend(p / "LibreOffice" / "program" / "soffice.exe" for p in _program_dizinleri())
    return next((p for p in adaylar if p.is_file()), None)


def artcraft_cli(ad: str) -> Path | None:
    ad = ad.lower()
    uygulama = {"wordcraft": "WordCraft", "gridcraft": "GridCraft"}[ad]
    adaylar = []
    paket_dizin = kok() / "artcraft" / ad
    if paket_dizin.is_dir():
        adaylar.extend(p for p in paket_dizin.glob(f"**/{ad}-cli.exe") if p.is_file())
    for p in _sistem_program_dizinleri():
        ana = p / uygulama
        if (ana / f"{ad}-cli.exe").is_file():
            adaylar.append(ana / f"{ad}-cli.exe")
        if ana.is_dir():
            adaylar.extend(alt for alt in ana.glob(f"**/{ad}-cli.exe") if alt.is_file())
    if not adaylar:
        return None
    benzersiz = []
    gorulen = set()
    for p in adaylar:
        if p not in gorulen:
            benzersiz.append(p)
            gorulen.add(p)
    benzersiz.sort(key=_surum_anahtari, reverse=True)
    return benzersiz[0]


def djvulibre(ad: str) -> Path | None:
    adaylar = [kok() / "araclar" / "djvulibre" / f"{ad}.exe"]
    adaylar.extend(p / "DjVuLibre" / f"{ad}.exe"
                   for p in _program_dizinleri(("ProgramFiles(x86)", "ProgramFiles")))
    return next((p for p in adaylar if p.is_file()), None)


def yedizip() -> Path | None:
    adaylar = [kok() / "araclar" / "7z" / "7z.exe"]
    for p in _sistem_program_dizinleri():
        adaylar.append(p / "7-Zip" / "7z.exe")
    bulunanlar = [p for p in adaylar if p.is_file()]
    if not bulunanlar:
        return None
    bulunanlar.sort(key=_surum_anahtari, reverse=True)
    return bulunanlar[0]


def ortak(ad: str) -> Path:
    return _gerekli(kok() / "ortak" / ad, f"Ortak dosya ({ad})")



def ceviri_modeli() -> Path:
    return kok() / "modeller" / "nllb-200-distilled-1.3B-ct2-int8"
