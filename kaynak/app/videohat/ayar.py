"""ayar.toml okuma. Eksik alana varsayılan, bilinmeyen alana uyarı."""
import tomllib
import warnings
from dataclasses import dataclass, field, fields
from pathlib import Path

from .hatalar import AyarHatasi


@dataclass
class SttAyar:
    gpu_model: str = "large-v3"
    cpu_model: str = "medium"
    beam_size: int = 5
    vad: bool = True
    vad_max_konusma_sn: float = 20.0
    vad_bosluk_ms: int = 500
    vad_konusma_pad_ms: int = 400
    halusinasyon_esik_sn: float = 2.0
    guven_esik: float = 0.45
    guven_kisa_esik: float = 0.30
    terimler_dosyasi: str = "terimler.txt"
    baglam_tasi: bool = True
    ikinci_gecis: bool = True
    ikinci_gecis_esik: float = 0.60
    ikinci_gecis_en_cok_oran: float = 0.25
    parca_siniri_tekrar_temizle: bool = True
    kisa_altyazi_uzat: bool = True
    bosluk_doldur: bool = True
    bosluk_min_sn: float = 30.0
    bosluk_aktif_orani: float = 0.40
    bosluk_enerji_db: float = 3.0
    bosluk_en_cok_oran: float = 1.0


@dataclass
class SesAyar:
    highpass_hz: int = 80
    afftdn_nf: int = -25
    lufs: float = -16.0
    tp: float = -1.5
    lra: int = 11
    agir_kuru_oran: float = 0.2


_GECERLI_UPSCALE_MODELLER = {"hizli", "agir", "genel"}
_GECERLI_ARA_FORMATLAR = {"jpg", "png"}


@dataclass
class GoruntuAyar:
    etkin: bool = False
    crf: int = 20
    nvenc: bool = True
    upscale_model: str = "hizli"
    hedef_yukseklik: int = 1080
    parca_sn: int = 120
    ara_format: str = "jpg"

    def __post_init__(self) -> None:
        if self.upscale_model not in _GECERLI_UPSCALE_MODELLER:
            raise AyarHatasi(f"Bilinmeyen upscale_model: {self.upscale_model}")
        if self.ara_format not in _GECERLI_ARA_FORMATLAR:
            raise AyarHatasi(f"Bilinmeyen ara_format: {self.ara_format}")
        if self.hedef_yukseklik <= 0:
            raise AyarHatasi(f"Geçersiz hedef_yukseklik: {self.hedef_yukseklik}")
        if self.parca_sn <= 0:
            raise AyarHatasi(f"Geçersiz parca_sn: {self.parca_sn}")


@dataclass
class IzleAyar:
    yoklama_sn: int = 5
    sabit_tur: int = 3


@dataclass
class GenelAyar:
    ara_dosya_sakla: bool = False
    cpu_threads: int = 0


@dataclass
class Ayar:
    kok: Path
    stt: SttAyar = field(default_factory=SttAyar)
    ses: SesAyar = field(default_factory=SesAyar)
    goruntu: GoruntuAyar = field(default_factory=GoruntuAyar)
    izle: IzleAyar = field(default_factory=IzleAyar)
    genel: GenelAyar = field(default_factory=GenelAyar)


_BOLUMLER = {"stt": SttAyar, "ses": SesAyar, "goruntu": GoruntuAyar,
             "izle": IzleAyar, "genel": GenelAyar}


def yukle(kok: Path) -> Ayar:
    yol = kok / "ayar.toml"
    ayar = Ayar(kok=kok)
    if not yol.exists():
        return ayar
    try:
        with open(yol, "rb") as f:
            ham = tomllib.load(f)
    except tomllib.TOMLDecodeError as e:
        raise AyarHatasi(f"ayar.toml bozuk: {e}") from e
    for bolum, sinif in _BOLUMLER.items():
        veri = ham.get(bolum, {})
        geçerli = {f.name for f in fields(sinif)}
        for k in veri:
            if k not in geçerli:
                warnings.warn(f"ayar.toml: bilinmeyen alan [{bolum}] {k}")
        setattr(ayar, bolum, sinif(**{k: v for k, v in veri.items() if k in geçerli}))
    for k in ham:
        if k not in _BOLUMLER:
            warnings.warn(f"ayar.toml: bilinmeyen bölüm [{k}]")
    return ayar
