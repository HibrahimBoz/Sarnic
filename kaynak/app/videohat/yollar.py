"""Paket içi yol çözümleme. Her yol paket köküne göredir; sistem PATH'ine güvenilmez."""
import os
from pathlib import Path

from .hatalar import OrtamHatasi


def kok() -> Path:
    """Paket kökü. VIDEOHAT_KOK ortam değişkeni varsa o, yoksa app\\videohat'ın iki üstü."""
    env = os.environ.get("VIDEOHAT_KOK")
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


def libreoffice() -> Path | None:
    p = kok() / "araclar" / "libreoffice" / "program" / "soffice.exe"
    return p if p.is_file() else None


def ortak(ad: str) -> Path:
    return _gerekli(kok() / "ortak" / ad, f"Ortak dosya ({ad})")
