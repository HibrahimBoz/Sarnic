"""Çalıştırma ortamına göre Whisper cihazı ve modelini seçer."""
import subprocess
from dataclasses import dataclass

from . import yollar
from .ayar import Ayar
from .hatalar import OrtamHatasi


@dataclass
class CihazSecimi:
    device: str
    compute_type: str
    model_ad: str
    neden: str


_MODELLER = ("large-v3", "medium", "turbo")

# CUDA yüklemesi bir kez başarısız olduysa aynı süreçte tekrar denenmez
# (ctranslate2 bozuk CUDA durumunda ikinci denemede takılabiliyor).
_cuda_kapali = False


def cuda_kapat() -> None:
    global _cuda_kapali
    _cuda_kapali = True


def gpu_bilgi() -> list[dict] | None:
    """nvidia-smi çıktısından GPU adını ve boş belleğini okur."""
    try:
        sonuc = subprocess.run(
            [
                "nvidia-smi",
                "--query-gpu=memory.free,name",
                "--format=csv,noheader,nounits",
            ],
            check=True,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        )
        bilgiler = []
        for satir in sonuc.stdout.splitlines():
            bolumler = [parca.strip() for parca in satir.split(",", 1)]
            if len(bolumler) != 2:
                return None
            bilgiler.append({"ad": bolumler[1], "bos_mb": int(bolumler[0])})
        return bilgiler or None
    except Exception:
        return None


def _var_olan_model(tercih: str, sabit: bool = False) -> str:
    if sabit:
        if not yollar.model_var_mi(tercih):
            raise OrtamHatasi(f"'{tercih}' modeli bulunamadı.")
        return tercih

    if tercih not in _MODELLER:
        tercih = "medium"
    if tercih in _MODELLER:
        baslangic = _MODELLER.index(tercih)
        siralama = _MODELLER[baslangic:]
    else:
        siralama = ()
    for ad in siralama:
        if yollar.model_var_mi(ad):
            return ad
    for ad in _MODELLER:
        if ad not in siralama and yollar.model_var_mi(ad):
            return ad
    raise OrtamHatasi("Kullanılabilir Whisper modeli bulunamadı.")


def sec(
    ayar: Ayar,
    zorla_cihaz: str | None = None,
    zorla_model: str | None = None,
) -> CihazSecimi:
    """CUDA belleğine ve yerel model dosyalarına göre cihaz seçer."""
    if zorla_model is not None and zorla_model not in _MODELLER:
        raise OrtamHatasi(f"Geçersiz Whisper modeli: {zorla_model}")
    if zorla_cihaz not in (None, "cpu", "cuda"):
        raise OrtamHatasi(f"Geçersiz cihaz: {zorla_cihaz}")

    if _cuda_kapali and zorla_cihaz is None:
        zorla_cihaz = "cpu"

    if zorla_cihaz == "cpu":
        secilen_model = _var_olan_model(
            zorla_model or ayar.stt.cpu_model,
            sabit=zorla_model is not None,
        )
        return CihazSecimi(
            "cpu",
            "int8",
            secilen_model,
            "CPU zorlandı.",
        )

    try:
        import ctranslate2

        cuda_var = ctranslate2.get_cuda_device_count() > 0
    except Exception:
        cuda_var = False

    if cuda_var:
        bilgiler = gpu_bilgi()
        bellek = bilgiler[0]["bos_mb"] if bilgiler else None
        if bellek is None or bellek < 1800:
            cuda_var = False
            cpu_neden = (
                "GPU belleği okunamadı; CPU kullanılıyor."
                if bellek is None
                else "GPU belleği yetersiz; CPU kullanılıyor."
            )
        elif bellek >= 5500:
            compute_type = "float16"
            gpu_model = ayar.stt.gpu_model
        elif bellek >= 3000:
            compute_type = "int8_float16"
            gpu_model = ayar.stt.gpu_model
        else:
            compute_type = "int8_float16"
            gpu_model = "medium"

    if cuda_var:
        tercih = zorla_model or gpu_model
        secilen_model = _var_olan_model(tercih, sabit=zorla_model is not None)
        neden = f"CUDA seçildi; {bellek} MB boş bellek."
        if secilen_model != tercih:
            neden += f" Model dosyası bulunmadığından {secilen_model} kullanılıyor."
        return CihazSecimi("cuda", compute_type, secilen_model, neden)

    cpu_neden = locals().get("cpu_neden", "CUDA kullanılamıyor; CPU seçildi.")
    secilen_model = _var_olan_model(
        zorla_model or ayar.stt.cpu_model,
        sabit=zorla_model is not None,
    )
    neden = cpu_neden
    if secilen_model != (zorla_model or ayar.stt.cpu_model):
        neden += f" CPU modeli bulunmadığından {secilen_model} kullanılıyor."
    return CihazSecimi("cpu", "int8", secilen_model, neden)
