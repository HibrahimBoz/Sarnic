"""Sarnıç Atölye: çevrimdışı Türkçe video transkript + ses/görüntü iyileştirme hattı."""
import os
from pathlib import Path


def _cuda_dll_ekle() -> None:
    """cuda\\ klasörünü, ctranslate2 import edilmeden ÖNCE DLL arama yoluna ekler."""
    kok = Path(os.environ.get("SARNICATOLYE_KOK") or Path(__file__).resolve().parent.parent.parent)
    cuda = kok / "cuda"
    if cuda.is_dir():
        os.environ["PATH"] = str(cuda) + os.pathsep + os.environ.get("PATH", "")
        if hasattr(os, "add_dll_directory"):
            os.add_dll_directory(str(cuda))


_cuda_dll_ekle()
os.environ.setdefault("HF_HUB_OFFLINE", "1")
os.environ.setdefault("TRANSFORMERS_OFFLINE", "1")
os.environ.setdefault("HF_HUB_DISABLE_TELEMETRY", "1")
