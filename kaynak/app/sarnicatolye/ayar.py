"""ayar.toml okuma. Eksik alana varsayılan, bilinmeyen alana uyarı."""
import tomllib
import warnings
import math
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
    # "otomatik": konuşma dili her kayıtta `diller` arasından Whisper ile seçilir; ya da sabit kod ("tr").
    dil: str = "otomatik"
    diller: list[str] = field(default_factory=lambda: ["tr", "en", "az", "ru"])

    def __post_init__(self) -> None:
        if not isinstance(self.dil, str) or not self.dil.strip():
            raise AyarHatasi('ayar.toml: [stt] dil "otomatik" ya da dil kodu olmalı (ör. "tr")')
        if not isinstance(self.diller, list):
            raise AyarHatasi('ayar.toml: [stt] diller liste olmalı (ör. ["tr", "en"])')
        self.dil = self.dil.strip().lower()
        self.diller = [str(d).strip().lower() for d in self.diller if str(d).strip()]
        if not self.diller:
            raise AyarHatasi("ayar.toml: [stt] diller boş olamaz")


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


def _sayi(ad, deger, alt, ust=None, tam=False):
    if (isinstance(deger, bool) or not isinstance(deger, (int, float))
            or not math.isfinite(deger) or deger < alt
            or (ust is not None and deger > ust) or (tam and not isinstance(deger, int))):
        raise AyarHatasi(f"Geçersiz {ad}: {deger}")


@dataclass
class DizinAyar:
    kokler: list[str] = field(default_factory=list)
    depo: str = ""
    haric: list[str] = field(default_factory=lambda: [
        "_sarnic", "_veridedektifi", "_bulgu", "$RECYCLE.BIN", "System Volume Information", "_ara", ".git", "node_modules"])
    calisma_db: str = ""
    yayin_dk: float = 30
    tarama_dk: float = 15
    max_dosya_mb: int = 2048
    isci: int = 0
    sakla_yayin: int = 3

    def __post_init__(self):
        for ad in ("kokler", "haric"):
            d = getattr(self, ad)
            if not isinstance(d, list) or not all(isinstance(s, str) and s for s in d):
                raise AyarHatasi(f"Geçersiz {ad}: metin listesi gerekli")
        for ad in ("depo", "calisma_db"):
            if not isinstance(getattr(self, ad), str):
                raise AyarHatasi(f"Geçersiz {ad}: metin gerekli")
        for ad in ("yayin_dk", "tarama_dk"):
            _sayi(ad, getattr(self, ad), 0.000001)
        _sayi("max_dosya_mb", self.max_dosya_mb, 1, tam=True)
        _sayi("isci", self.isci, 0, tam=True)
        _sayi("sakla_yayin", self.sakla_yayin, 1, tam=True)


@dataclass
class OcrAyar:
    profil: str = "uyarlamali"
    dpi: int = 300
    min_metin: int = 40
    ikinci_esik: float = 0.80
    cihaz: str = "auto"
    sayfa_zaman_asimi_sn: float = 180
    gomulu_gorsel: bool = True
    tesseract: bool = True
    sozluk: str = "sozluk\\tr_kelime.txt"
    unlu_uyumu: bool = True
    # Kiril (Rusça) satırlar için ikinci tanıma modeli; dosya yoksa sessizce atlanır.
    kiril: bool = True

    def __post_init__(self):
        if self.profil not in ("hizli", "uyarlamali", "kalite"):
            raise AyarHatasi(f"Geçersiz OCR profili: {self.profil}")
        if self.cihaz not in ("auto", "cpu", "cuda"):
            raise AyarHatasi(f"Geçersiz OCR cihazı: {self.cihaz}")
        _sayi("dpi", self.dpi, 1, tam=True)
        _sayi("min_metin", self.min_metin, 0, tam=True)
        _sayi("ikinci_esik", self.ikinci_esik, 0, 1)
        _sayi("sayfa_zaman_asimi_sn", self.sayfa_zaman_asimi_sn, 0.000001)
        for ad in ("gomulu_gorsel", "tesseract", "kiril"):
            if not isinstance(getattr(self, ad), bool):
                raise AyarHatasi(f"Geçersiz {ad}: doğru/yanlış gerekli")
        if not isinstance(self.sozluk, str):
            raise AyarHatasi("Geçersiz OCR sözlük yolu")


@dataclass
class CeviriAyar:
    etkin: bool = True
    model: str = ""
    diller: tuple[str, ...] = ("en", "az", "ru")
    batch: int = 16

    def __post_init__(self) -> None:
        if not isinstance(self.etkin, bool):
            raise AyarHatasi("Geçersiz ceviri.etkin: doğru/yanlış gerekli")
        if not isinstance(self.model, str):
            raise AyarHatasi("Geçersiz ceviri.model: metin gerekli")
        if isinstance(self.diller, (list, tuple, set)):
            self.diller = tuple(str(d).strip().lower() for d in self.diller if str(d).strip())
        else:
            raise AyarHatasi("Geçersiz ceviri.diller: liste ya da demet gerekli")
        _sayi("batch", self.batch, 1, tam=True)


@dataclass
class Ayar:
    kok: Path
    stt: SttAyar = field(default_factory=SttAyar)
    ses: SesAyar = field(default_factory=SesAyar)
    goruntu: GoruntuAyar = field(default_factory=GoruntuAyar)
    izle: IzleAyar = field(default_factory=IzleAyar)
    genel: GenelAyar = field(default_factory=GenelAyar)
    dizin: DizinAyar = field(default_factory=DizinAyar)
    ocr: OcrAyar = field(default_factory=OcrAyar)
    ceviri: CeviriAyar = field(default_factory=CeviriAyar)


_BOLUMLER = {"stt": SttAyar, "ses": SesAyar, "goruntu": GoruntuAyar,
             "izle": IzleAyar, "genel": GenelAyar, "dizin": DizinAyar, "ocr": OcrAyar,
             "ceviri": CeviriAyar}


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
